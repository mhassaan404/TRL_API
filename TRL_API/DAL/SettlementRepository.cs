using Microsoft.Data.SqlClient;
using System.Data;
using System.Text;
using TRL_API.Data;
using TRL_API.Models;

namespace TRL_API.DAL
{
    // Move-out settlement of a tenancy (tenant + unit) whose lease has ended. Uses the existing invoices, payments and
    // deposit ledger; lease proration, termination and rent generation are not touched.
    public class SettlementRepository : ISettlementRepository
    {
        private readonly DbHelper _dbHelper;
        public SettlementRepository(DbHelper dbHelper) => _dbHelper = dbHelper;

        // Payments made at settlement that are not new money (excluded from cash reports and receipts)
        public const string DepositMethod = "Security Deposit";
        public const string CreditMethod = "Credit to Deposit";

        // Same "current lease" rule as Lease Management and Security Deposits
        private const string CurrentLeaseExists = @"EXISTS (SELECT 1 FROM TenantLeases cl WHERE cl.TenantId = @TenantId AND cl.UnitId = @UnitId
                  AND (cl.IsActive = 1 OR (cl.TerminationReason = 'Renewed' AND cl.BilledThrough >= CAST(GETDATE() AS DATE))))";

        // The tenancy's last real lease (cancelled leases never ran, so they are skipped)
        private const string LastLeaseSql = @"(SELECT TOP 1 ll.LeaseId FROM TenantLeases ll WHERE ll.TenantId = @TenantId AND ll.UnitId = @UnitId
                  AND ISNULL(ll.TerminationReason, N'') NOT IN (N'Lease cancelled', N'Renewal cancelled')
                  ORDER BY ll.StartDate DESC, ll.LeaseId DESC)";

        // Months a lease of this tenancy covers (up to its billed-through date) that have no rent invoice
        private const string UnbilledCte = @"
                ;WITH L AS (
                    SELECT LeaseId, StartDate, BilledThrough FROM TenantLeases
                    WHERE TenantId = @TenantId AND UnitId = @UnitId AND IsActive = 0 AND BilledThrough IS NOT NULL AND BilledThrough >= StartDate
                ),
                M AS (
                    SELECT LeaseId, DATEFROMPARTS(YEAR(StartDate), MONTH(StartDate), 1) AS MonthStart, BilledThrough FROM L
                    UNION ALL
                    SELECT LeaseId, DATEADD(MONTH, 1, MonthStart), BilledThrough FROM M WHERE DATEADD(MONTH, 1, MonthStart) <= BilledThrough
                )";
        private const string UnbilledSelect = @"
                SELECT m.LeaseId, m.MonthStart, c.Amount
                FROM M m CROSS APPLY dbo.LeaseMonthCharge(m.LeaseId, m.MonthStart) c
                WHERE c.Amount > 0 AND NOT EXISTS (SELECT 1 FROM RentInvoices ri WHERE ri.LeaseId = m.LeaseId AND ri.InvoiceMonth = m.MonthStart
                                                     AND ri.ChargeType IS NULL AND ri.StatusId <> 6)";

        private static SqlParameter[] Tenancy(int tenantId, int unitId) =>
            new[] { new SqlParameter("@TenantId", tenantId), new SqlParameter("@UnitId", unitId) };

        // Ended tenancies not settled yet: those with money still open (owed, credit or deposit), or ended in the last year
        public async Task<DataTable> GetCandidatesAsync() =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                ;WITH Ten AS (SELECT DISTINCT TenantId, UnitId FROM TenantLeases),
                X AS (
                    SELECT x.TenantId, x.UnitId, last.LeaseId, last.StartDate, last.EndDate, last.BilledThrough, last.TerminationReason,
                           ISNULL(o.Outstanding, 0) AS Outstanding, ISNULL(o.Credit, 0) AS Credit, ISNULL(d.Held, 0) AS Held
                    FROM Ten x
                    CROSS APPLY (SELECT TOP 1 ll.LeaseId, ll.StartDate, ll.EndDate, ll.BilledThrough, ll.TerminationReason FROM TenantLeases ll
                                 WHERE ll.TenantId = x.TenantId AND ll.UnitId = x.UnitId
                                   AND ISNULL(ll.TerminationReason, N'') NOT IN (N'Lease cancelled', N'Renewal cancelled')
                                 ORDER BY ll.StartDate DESC, ll.LeaseId DESC) last
                    OUTER APPLY (SELECT SUM(CASE WHEN b.Balance > 0 THEN b.Balance ELSE 0 END) AS Outstanding,
                                        SUM(CASE WHEN b.Balance < 0 THEN -b.Balance ELSE 0 END) AS Credit
                                 FROM RentInvoices ri CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) b
                                 WHERE ri.TenantId = x.TenantId AND ri.UnitId = x.UnitId AND ri.StatusId <> 6) o
                    OUTER APPLY (SELECT SUM(Amount) AS Held FROM SecurityDeposits sd WHERE sd.TenantId = x.TenantId AND sd.UnitId = x.UnitId) d
                    WHERE NOT EXISTS (SELECT 1 FROM TenantLeases cl WHERE cl.TenantId = x.TenantId AND cl.UnitId = x.UnitId
                                        AND (cl.IsActive = 1 OR (cl.TerminationReason = 'Renewed' AND cl.BilledThrough >= CAST(GETDATE() AS DATE))))
                      AND NOT EXISTS (SELECT 1 FROM MoveOutSettlements s WHERE s.LeaseId = last.LeaseId)
                )
                SELECT X.*, t.Name AS TenantName, t.Contact, b.BuildingName, f.FloorNumber, u.UnitNumber
                FROM X
                INNER JOIN Tenants t ON t.TenantId = X.TenantId
                INNER JOIN Units u ON u.UnitId = X.UnitId
                LEFT JOIN Floors f ON f.FloorId = u.FloorId
                LEFT JOIN Buildings b ON b.BuildingId = f.BuildingId
                WHERE X.Outstanding <> 0 OR X.Credit <> 0 OR X.Held <> 0
                   OR ISNULL(X.BilledThrough, X.EndDate) >= DATEADD(YEAR, -1, CAST(GETDATE() AS DATE))
                ORDER BY ISNULL(X.BilledThrough, X.EndDate) DESC, t.Name;");

        public async Task<DataTable> GetSettlementsAsync() =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT s.Id AS SettlementId, s.TenantId, s.UnitId, s.LeaseId, s.SettlementDate, s.OutstandingBefore, s.CreditBefore,
                       s.DeductionsTotal, s.DepositHeldBefore, s.DepositApplied, s.TenantOwes, s.RefundDue, s.FinalPaymentReceived, s.RefundPaid,
                       t.Name AS TenantName, b.BuildingName, u.UnitNumber, cu.Username AS SettledBy,
                       ISNULL((SELECT SUM(CASE WHEN bal.Balance > 0 THEN bal.Balance ELSE 0 END) FROM MoveOutSettlementLines l
                               CROSS APPLY dbo.InvoiceBalance(l.InvoiceId, 0) bal
                               WHERE l.SettlementId = s.Id AND l.LineType <> 'Credit'), 0) AS StillOwedNow
                FROM MoveOutSettlements s
                INNER JOIN Tenants t ON t.TenantId = s.TenantId
                INNER JOIN Units u ON u.UnitId = s.UnitId
                LEFT JOIN Floors f ON f.FloorId = u.FloorId
                LEFT JOIN Buildings b ON b.BuildingId = f.BuildingId
                LEFT JOIN Users cu ON cu.UserId = s.CreatedBy
                ORDER BY s.SettlementDate DESC, s.Id DESC;");

        // Preview: tenancy and last lease (+ state codes), open invoices, unbilled rent months
        public async Task<DataTable> GetPreviewHeaderAsync(int tenantId, int unitId) =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync($@"
                DECLARE @LeaseId INT = {LastLeaseSql};
                SELECT t.TenantId, t.Name AS TenantName, t.Contact, t.IsDeleted AS TenantDeleted, u.UnitId, u.UnitNumber, b.BuildingName, f.FloorNumber,
                       l.LeaseId, l.StartDate AS LeaseStartDate, l.EndDate AS LeaseEndDate, l.BilledThrough AS MoveOutDate, l.TerminationReason,
                       CAST(CASE WHEN {CurrentLeaseExists} THEN 1 ELSE 0 END AS BIT) AS HasCurrentLease,
                       (SELECT s.Id FROM MoveOutSettlements s WHERE s.LeaseId = @LeaseId) AS ExistingSettlementId,
                       ISNULL((SELECT SUM(Amount) FROM SecurityDeposits WHERE TenantId = @TenantId AND UnitId = @UnitId), 0) AS Held,
                       (SELECT AgreedAmount FROM SecurityDepositTerms WHERE TenantId = @TenantId AND UnitId = @UnitId) AS AgreedDeposit,
                       CAST(GETDATE() AS DATE) AS Today
                FROM Tenants t
                CROSS JOIN Units u
                LEFT JOIN Floors f ON f.FloorId = u.FloorId
                LEFT JOIN Buildings b ON b.BuildingId = f.BuildingId
                LEFT JOIN TenantLeases l ON l.LeaseId = @LeaseId
                WHERE t.TenantId = @TenantId AND u.UnitId = @UnitId;", Tenancy(tenantId, unitId));

        // Invoices of the tenancy with something still open: Balance > 0 owed, < 0 credit (overpaid)
        public async Task<DataTable> GetOpenInvoicesAsync(int tenantId, int unitId) =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT ri.Id AS InvoiceId, ri.InvoiceDate, ri.InvoiceMonth, ri.DueDate, ri.ChargeType, ri.Description, ri.LeaseId,
                       ri.TotalRent, ri.LateFeeCharged, b.Paid, b.Disc AS Discount, b.Balance, b.OpenLateFee
                FROM RentInvoices ri CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) b
                WHERE ri.TenantId = @TenantId AND ri.UnitId = @UnitId AND ri.StatusId <> 6 AND b.Balance <> 0
                ORDER BY ri.DueDate, ri.Id;", Tenancy(tenantId, unitId));

        public async Task<DataTable> GetUnbilledMonthsAsync(int tenantId, int unitId) =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync(UnbilledCte + UnbilledSelect + " ORDER BY m.MonthStart OPTION (MAXRECURSION 1000);",
                Tenancy(tenantId, unitId));

        // Result codes: OK (Id, ReceiptIds), NOT_FOUND, LEASE_ACTIVE, CHANGED, ALREADY_SETTLED, UNBILLED, DATE_BEFORE_MOVE_OUT,
        // FINAL_TOO_HIGH, REFUND_TOO_HIGH, NEGATIVE_HELD, BUSY. Everything happens in one transaction under the tenancy lock
        // (the same lock as Security Deposits), with the tenancy's invoices locked, so nothing can change in between.
        public async Task<(string Result, int? Id, string? ReceiptIds)> FinalizeAsync(FinalizeSettlementRequest r, int userId)
        {
            var deductions = r.Deductions ?? new List<SettlementDeductionInput>();
            var ps = new List<SqlParameter>
            {
                new("@TenantId", r.TenantId), new("@UnitId", r.UnitId), new("@LeaseId", r.LeaseId),
                Money("@ExpOutstanding", r.ExpectedOutstanding ?? 0), Money("@ExpCredit", r.ExpectedCredit ?? 0), Money("@ExpHeld", r.ExpectedHeld ?? 0),
                new("@Date", SqlDbType.Date) { Value = r.SettlementDate!.Value.Date },
                Text("@Notes", r.Notes, 500), new("@UserId", userId), new("@Ack", r.AcknowledgeUnbilled),
                Money("@FinalAmount", r.FinalPayment?.Amount ?? 0), Text("@FinalMethod", r.FinalPayment?.PaymentMethod, 30), Text("@FinalRef", r.FinalPayment?.Reference, 100),
                Money("@RefundAmount", r.Refund?.Amount ?? 0), Text("@RefundMethod", r.Refund?.PaymentMethod, 30), Text("@RefundRef", r.Refund?.Reference, 100),
            };
            var values = new StringBuilder();
            for (var i = 0; i < deductions.Count; i++)
            {
                if (i > 0) values.Append(", ");
                values.Append($"({i}, @DT{i}, @DA{i}, @DR{i})");
                ps.Add(Text($"@DT{i}", deductions[i].ChargeType, 50));
                ps.Add(Money($"@DA{i}", deductions[i].Amount!.Value));
                ps.Add(Text($"@DR{i}", deductions[i].Reason, 255));
            }
            var insertDeductions = deductions.Count == 0 ? "" : $"INSERT INTO @Ded (Seq, ChargeType, Amount, Reason) VALUES {values};";

            var sql = $@"
                SET XACT_ABORT ON;
                SET NOCOUNT ON;
                DECLARE @Ded TABLE (Seq INT, ChargeType NVARCHAR(50), Amount DECIMAL(18, 2), Reason NVARCHAR(255), InvoiceId INT);
                {insertDeductions}
                DECLARE @Open TABLE (InvoiceId INT PRIMARY KEY, DueDate DATE, LineType NVARCHAR(20), BalanceBefore DECIMAL(18, 2),
                                     Remaining DECIMAL(18, 2), Applied DECIMAL(18, 2) DEFAULT 0, Cash DECIMAL(18, 2) DEFAULT 0, CashPaymentId INT);

                BEGIN TRAN;
                DECLARE @Lock INT, @Resource NVARCHAR(100) = CONCAT(N'TRL_Deposit_', @TenantId, N'_', @UnitId);
                EXEC @Lock = sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
                IF @Lock < 0 BEGIN ROLLBACK; SELECT 'BUSY' AS Result, CAST(NULL AS INT) AS Id, CAST(NULL AS NVARCHAR(400)) AS ReceiptIds; RETURN; END

                DECLARE @Last INT = {LastLeaseSql};
                IF @Last IS NULL BEGIN ROLLBACK; SELECT 'NOT_FOUND' AS Result, CAST(NULL AS INT) AS Id, CAST(NULL AS NVARCHAR(400)) AS ReceiptIds; RETURN; END
                IF {CurrentLeaseExists} BEGIN ROLLBACK; SELECT 'LEASE_ACTIVE' AS Result, CAST(NULL AS INT) AS Id, CAST(NULL AS NVARCHAR(400)) AS ReceiptIds; RETURN; END
                IF EXISTS (SELECT 1 FROM MoveOutSettlements WHERE LeaseId = @Last)
                BEGIN ROLLBACK; SELECT 'ALREADY_SETTLED' AS Result, (SELECT Id FROM MoveOutSettlements WHERE LeaseId = @Last) AS Id, CAST(NULL AS NVARCHAR(400)) AS ReceiptIds; RETURN; END
                IF @Last <> @LeaseId BEGIN ROLLBACK; SELECT 'CHANGED' AS Result, CAST(NULL AS INT) AS Id, CAST(NULL AS NVARCHAR(400)) AS ReceiptIds; RETURN; END

                DECLARE @MoveOut DATE = (SELECT ISNULL(BilledThrough, EndDate) FROM TenantLeases WHERE LeaseId = @Last);
                IF @Date < @MoveOut BEGIN ROLLBACK; SELECT 'DATE_BEFORE_MOVE_OUT' AS Result, CAST(NULL AS INT) AS Id, CONVERT(NVARCHAR(400), @MoveOut, 23) AS ReceiptIds; RETURN; END

                -- Lock the tenancy's invoices, then read the figures the admin reviewed
                DECLARE @Locked INT = (SELECT COUNT(*) FROM RentInvoices WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = @TenantId AND UnitId = @UnitId);
                INSERT INTO @Open (InvoiceId, DueDate, LineType, BalanceBefore, Remaining)
                SELECT ri.Id, ri.DueDate, CASE WHEN b.Balance < 0 THEN 'Credit' ELSE 'Outstanding' END, b.Balance, b.Balance
                FROM RentInvoices ri CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) b
                WHERE ri.TenantId = @TenantId AND ri.UnitId = @UnitId AND ri.StatusId <> 6 AND b.Balance <> 0;

                DECLARE @Outstanding DECIMAL(18, 2) = ISNULL((SELECT SUM(BalanceBefore) FROM @Open WHERE LineType = 'Outstanding'), 0);
                DECLARE @Credit DECIMAL(18, 2) = ISNULL((SELECT -SUM(BalanceBefore) FROM @Open WHERE LineType = 'Credit'), 0);
                DECLARE @Held DECIMAL(18, 2) = ISNULL((SELECT SUM(Amount) FROM SecurityDeposits WHERE TenantId = @TenantId AND UnitId = @UnitId), 0);
                IF @Outstanding <> @ExpOutstanding OR @Credit <> @ExpCredit OR @Held <> @ExpHeld
                BEGIN ROLLBACK; SELECT 'CHANGED' AS Result, CAST(NULL AS INT) AS Id, CAST(NULL AS NVARCHAR(400)) AS ReceiptIds; RETURN; END
                IF @Held < 0 BEGIN ROLLBACK; SELECT 'NEGATIVE_HELD' AS Result, CAST(NULL AS INT) AS Id, CAST(NULL AS NVARCHAR(400)) AS ReceiptIds; RETURN; END

                {UnbilledCte}
                SELECT @Locked = COUNT(*) FROM ({UnbilledSelect}) ub OPTION (MAXRECURSION 1000);
                IF @Locked > 0 AND @Ack = 0 BEGIN ROLLBACK; SELECT 'UNBILLED' AS Result, @Locked AS Id, CAST(NULL AS NVARCHAR(400)) AS ReceiptIds; RETURN; END

                -- Settlement figures: the deposit (plus any credit) pays what is owed (outstanding + deductions)
                DECLARE @Deductions DECIMAL(18, 2) = ISNULL((SELECT SUM(Amount) FROM @Ded), 0);
                DECLARE @Pool DECIMAL(18, 2) = @Held + @Credit, @Owed DECIMAL(18, 2) = @Outstanding + @Deductions;
                DECLARE @Applied DECIMAL(18, 2) = CASE WHEN @Pool < @Owed THEN @Pool ELSE @Owed END;
                DECLARE @TenantOwes DECIMAL(18, 2) = @Owed - @Applied, @RefundDue DECIMAL(18, 2) = @Pool - @Applied;
                IF @FinalAmount > @TenantOwes BEGIN ROLLBACK; SELECT 'FINAL_TOO_HIGH' AS Result, CAST(NULL AS INT) AS Id, CAST(@TenantOwes AS NVARCHAR(400)) AS ReceiptIds; RETURN; END
                IF @RefundAmount > @RefundDue BEGIN ROLLBACK; SELECT 'REFUND_TOO_HIGH' AS Result, CAST(NULL AS INT) AS Id, CAST(@RefundDue AS NVARCHAR(400)) AS ReceiptIds; RETURN; END

                INSERT INTO MoveOutSettlements (TenantId, UnitId, LeaseId, SettlementDate, OutstandingBefore, CreditBefore, DeductionsTotal,
                                                DepositHeldBefore, DepositApplied, TenantOwes, RefundDue, FinalPaymentReceived, RefundPaid, Notes, CreatedBy)
                VALUES (@TenantId, @UnitId, @Last, @Date, @Outstanding, @Credit, @Deductions, @Held, @Applied, @TenantOwes, @RefundDue,
                        @FinalAmount, @RefundAmount, @Notes, @UserId);
                DECLARE @SId INT = SCOPE_IDENTITY();
                DECLARE @Tag NVARCHAR(60) = CONCAT(N'move-out settlement #', @SId);

                -- 1. Credits (overpaid invoices) move into the deposit pool: the invoice is brought back to 0
                INSERT INTO Payments (TenantId, PaymentAmount, PaymentDate, RentInvoiceId, PaymentMethod, Notes, DiscountAmount, DiscountPercent, IsLateFeeWaived, CreatedBy, CreatedAt)
                SELECT @TenantId, o.BalanceBefore, @Date, o.InvoiceId, N'{CreditMethod}', CONCAT(N'Credit moved to the security deposit at ', @Tag), 0, 0, 0, @UserId, GETDATE()
                FROM @Open o WHERE o.LineType = 'Credit';
                INSERT INTO SecurityDeposits (TenantId, UnitId, LeaseId, EntryType, Amount, EntryDate, Notes, CreatedBy, SettlementId, InvoiceId)
                SELECT @TenantId, @UnitId, @Last, 'Credit Transfer', -o.BalanceBefore, @Date, CONCAT(N'Credit from invoice #', o.InvoiceId, N' at ', @Tag), @UserId, @SId, o.InvoiceId
                FROM @Open o WHERE o.LineType = 'Credit';

                -- 2. Deductions are billed as extra-charge invoices (no late fee), so they stay in the invoice history
                DECLARE @Seq INT = 0, @NewInv INT, @Max INT = (SELECT COUNT(*) FROM @Ded);
                DECLARE @Mult DECIMAL(5, 2) = (SELECT MaxLateFeeMultiplier FROM LateFeeSettings WHERE Id = 1);
                WHILE @Seq < @Max
                BEGIN
                    INSERT INTO RentInvoices (TenantId, LeaseId, UnitId, TotalRent, PendingAmount, OverPaidAmount, InvoiceDate, DueDate, StatusId,
                                              Description, ChargeType, CreatedAt, LateFeePerDay, LateFeeMaxMultiplier, RelatedInvoiceId)
                    SELECT @TenantId, NULL, @UnitId, d.Amount, d.Amount, 0, @Date, @Date, 2, d.Reason, d.ChargeType, GETDATE(), 0, @Mult, NULL
                    FROM @Ded d WHERE d.Seq = @Seq;
                    SET @NewInv = SCOPE_IDENTITY();
                    UPDATE @Ded SET InvoiceId = @NewInv WHERE Seq = @Seq;
                    INSERT INTO @Open (InvoiceId, DueDate, LineType, BalanceBefore, Remaining)
                    SELECT @NewInv, @Date, 'Deduction', Amount, Amount FROM @Ded WHERE Seq = @Seq;
                    SET @Seq += 1;
                END
                INSERT INTO MoveOutSettlementDeductions (SettlementId, ChargeType, Amount, Reason, InvoiceId)
                SELECT @SId, ChargeType, Amount, Reason, InvoiceId FROM @Ded ORDER BY Seq;

                -- 3. The deposit pays open invoices, oldest due date first (deductions last, they are due today)
                DECLARE @Left DECIMAL(18, 2) = @Applied, @Inv INT, @Amt DECIMAL(18, 2);
                WHILE @Left > 0
                BEGIN
                    SELECT TOP 1 @Inv = InvoiceId, @Amt = CASE WHEN Remaining < @Left THEN Remaining ELSE @Left END
                    FROM @Open WHERE LineType <> 'Credit' AND Remaining > 0 ORDER BY DueDate, InvoiceId;
                    IF @@ROWCOUNT = 0 BREAK;
                    INSERT INTO Payments (TenantId, PaymentAmount, PaymentDate, RentInvoiceId, PaymentMethod, Notes, DiscountAmount, DiscountPercent, IsLateFeeWaived, CreatedBy, CreatedAt)
                    VALUES (@TenantId, @Amt, @Date, @Inv, N'{DepositMethod}', CONCAT(N'Paid from the security deposit at ', @Tag), 0, 0, 0, @UserId, GETDATE());
                    INSERT INTO SecurityDeposits (TenantId, UnitId, LeaseId, EntryType, Amount, EntryDate, Notes, CreatedBy, SettlementId, InvoiceId)
                    VALUES (@TenantId, @UnitId, @Last, 'Applied', -@Amt, @Date, CONCAT(N'Applied to invoice #', @Inv, N' at ', @Tag), @UserId, @SId, @Inv);
                    UPDATE @Open SET Remaining -= @Amt, Applied += @Amt WHERE InvoiceId = @Inv;
                    SET @Left -= @Amt;
                END

                -- 4. Money actually received from the tenant now (optional): real payments, with receipts
                DECLARE @Receipts NVARCHAR(400) = N'';
                SET @Left = @FinalAmount;
                WHILE @Left > 0
                BEGIN
                    SELECT TOP 1 @Inv = InvoiceId, @Amt = CASE WHEN Remaining < @Left THEN Remaining ELSE @Left END
                    FROM @Open WHERE LineType <> 'Credit' AND Remaining > 0 ORDER BY DueDate, InvoiceId;
                    IF @@ROWCOUNT = 0 BREAK;
                    INSERT INTO Payments (TenantId, PaymentAmount, PaymentDate, RentInvoiceId, PaymentMethod, Notes, DiscountAmount, DiscountPercent, IsLateFeeWaived, CreatedBy, CreatedAt)
                    VALUES (@TenantId, @Amt, @Date, @Inv, @FinalMethod,
                            CONCAT(N'Final payment at ', @Tag, CASE WHEN @FinalRef IS NULL THEN N'' ELSE CONCAT(N'. Ref: ', @FinalRef) END), 0, 0, 0, @UserId, GETDATE());
                    DECLARE @PayId INT = SCOPE_IDENTITY();
                    UPDATE @Open SET Remaining -= @Amt, Cash += @Amt, CashPaymentId = @PayId WHERE InvoiceId = @Inv;
                    SET @Receipts = CONCAT(@Receipts, CASE WHEN @Receipts = N'' THEN N'' ELSE N',' END, @PayId);
                    SET @Left -= @Amt;
                END

                -- 5. Deposit money actually paid back now (optional)
                IF @RefundAmount > 0
                    INSERT INTO SecurityDeposits (TenantId, UnitId, LeaseId, EntryType, Amount, EntryDate, PaymentMethod, Reference, Notes, CreatedBy, SettlementId)
                    VALUES (@TenantId, @UnitId, @Last, 'Refund', -@RefundAmount, @Date, @RefundMethod, @RefundRef, CONCAT(N'Refund at ', @Tag), @UserId, @SId);

                INSERT INTO MoveOutSettlementLines (SettlementId, InvoiceId, LineType, BalanceBefore, DepositApplied, CashReceived, CashPaymentId)
                SELECT @SId, InvoiceId, LineType, BalanceBefore, Applied, Cash, CashPaymentId FROM @Open;

                -- Invoice status/pending amounts from their payments (the same recalculation as every payment)
                {InvoiceSql.Recalc}
                WHERE ri.Id IN (SELECT InvoiceId FROM @Open) AND ri.StatusId <> 6;

                COMMIT;
                SELECT 'OK' AS Result, @SId AS Id, NULLIF(@Receipts, N'') AS ReceiptIds;";

            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(sql, ps.ToArray());
            var row = dt.Rows[0];
            return (row["Result"].ToString()!, row["Id"] == DBNull.Value ? null : Convert.ToInt32(row["Id"]),
                    row["ReceiptIds"] == DBNull.Value ? null : row["ReceiptIds"].ToString());
        }

        // A refund paid after the settlement. Result codes: OK, NOT_FOUND, TOO_HIGH (Info = refundable now), DATE_BEFORE, DUPLICATE, BUSY
        public async Task<(string Result, decimal? Info)> RecordRefundAsync(RecordSettlementRefundRequest r, int userId)
        {
            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SET XACT_ABORT ON;
                DECLARE @TenantId INT, @UnitId INT, @LeaseId INT, @Due DECIMAL(18, 2), @SDate DATE;
                SELECT @TenantId = TenantId, @UnitId = UnitId, @LeaseId = LeaseId, @Due = RefundDue - RefundPaid, @SDate = SettlementDate
                FROM MoveOutSettlements WHERE Id = @SId;
                IF @TenantId IS NULL BEGIN SELECT 'NOT_FOUND' AS Result, CAST(NULL AS DECIMAL(18, 2)) AS Info; RETURN; END

                BEGIN TRAN;
                DECLARE @Lock INT, @Resource NVARCHAR(100) = CONCAT(N'TRL_Deposit_', @TenantId, N'_', @UnitId);
                EXEC @Lock = sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
                IF @Lock < 0 BEGIN ROLLBACK; SELECT 'BUSY' AS Result, CAST(NULL AS DECIMAL(18, 2)) AS Info; RETURN; END
                SELECT @Due = RefundDue - RefundPaid FROM MoveOutSettlements WITH (UPDLOCK) WHERE Id = @SId;

                IF @Date < @SDate BEGIN ROLLBACK; SELECT 'DATE_BEFORE' AS Result, CAST(NULL AS DECIMAL(18, 2)) AS Info; RETURN; END
                IF EXISTS (SELECT 1 FROM SecurityDeposits WHERE SettlementId = @SId AND EntryType = 'Refund' AND Amount = -@Amount
                             AND EntryDate = @Date AND CreatedAt >= DATEADD(SECOND, -60, GETDATE()))
                BEGIN ROLLBACK; SELECT 'DUPLICATE' AS Result, CAST(NULL AS DECIMAL(18, 2)) AS Info; RETURN; END
                -- Never more than what is still refundable, nor more than is actually held
                DECLARE @Held DECIMAL(18, 2) = ISNULL((SELECT SUM(Amount) FROM SecurityDeposits WHERE TenantId = @TenantId AND UnitId = @UnitId), 0);
                DECLARE @Can DECIMAL(18, 2) = CASE WHEN @Held < @Due THEN @Held ELSE @Due END;
                IF @Amount > @Can BEGIN ROLLBACK; SELECT 'TOO_HIGH' AS Result, CASE WHEN @Can < 0 THEN 0 ELSE @Can END AS Info; RETURN; END

                INSERT INTO SecurityDeposits (TenantId, UnitId, LeaseId, EntryType, Amount, EntryDate, PaymentMethod, Reference, Notes, CreatedBy, SettlementId)
                VALUES (@TenantId, @UnitId, @LeaseId, 'Refund', -@Amount, @Date, @Method, @Reference, CONCAT(N'Refund for move-out settlement #', @SId), @UserId, @SId);
                UPDATE MoveOutSettlements SET RefundPaid += @Amount WHERE Id = @SId;
                COMMIT;
                SELECT 'OK' AS Result, CAST(NULL AS DECIMAL(18, 2)) AS Info;",
                new[]
                {
                    new SqlParameter("@SId", r.SettlementId), Money("@Amount", r.Amount!.Value),
                    new SqlParameter("@Date", SqlDbType.Date) { Value = r.RefundDate!.Value.Date },
                    Text("@Method", r.PaymentMethod, 30), Text("@Reference", r.Reference, 100), new SqlParameter("@UserId", userId),
                });
            var row = dt.Rows[0];
            return (row["Result"].ToString()!, row["Info"] == DBNull.Value ? null : Convert.ToDecimal(row["Info"]));
        }

        // Statement data: header (with tenant, unit, lease), lines (invoice snapshot), deductions, refunds
        public async Task<DataTable> GetSettlementAsync(int id) =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT s.Id AS SettlementId, s.TenantId, s.UnitId, s.LeaseId, s.SettlementDate, s.OutstandingBefore, s.CreditBefore, s.DeductionsTotal,
                       s.DepositHeldBefore, s.DepositApplied, s.TenantOwes, s.RefundDue, s.FinalPaymentReceived, s.RefundPaid, s.Notes, s.CreatedAt,
                       cu.Username AS SettledBy,
                       t.Name AS TenantName, t.TenantType, t.ContactPerson, t.Contact, t.Email, t.CnicNtn, t.Address AS TenantAddress,
                       b.BuildingName, b.Address AS BuildingAddress, f.FloorNumber, u.UnitNumber,
                       l.StartDate AS LeaseStartDate, l.EndDate AS LeaseEndDate, l.BilledThrough AS MoveOutDate, l.TerminationReason,
                       ISNULL((SELECT SUM(CASE WHEN bal.Balance > 0 THEN bal.Balance ELSE 0 END) FROM MoveOutSettlementLines ml
                               CROSS APPLY dbo.InvoiceBalance(ml.InvoiceId, 0) bal WHERE ml.SettlementId = s.Id AND ml.LineType <> 'Credit'), 0) AS StillOwedNow
                FROM MoveOutSettlements s
                INNER JOIN Tenants t ON t.TenantId = s.TenantId
                INNER JOIN Units u ON u.UnitId = s.UnitId
                LEFT JOIN Floors f ON f.FloorId = u.FloorId
                LEFT JOIN Buildings b ON b.BuildingId = f.BuildingId
                LEFT JOIN TenantLeases l ON l.LeaseId = s.LeaseId
                LEFT JOIN Users cu ON cu.UserId = s.CreatedBy
                WHERE s.Id = @Id;", new[] { new SqlParameter("@Id", id) });

        public async Task<DataTable> GetSettlementLinesAsync(int id) =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT ml.InvoiceId, ml.LineType, ml.BalanceBefore, ml.DepositApplied, ml.CashReceived, ml.CashPaymentId,
                       ri.InvoiceDate, ri.InvoiceMonth, ri.DueDate, ri.ChargeType, ri.Description, ri.TotalRent, ri.LateFeeCharged,
                       d.Reason AS DeductionReason
                FROM MoveOutSettlementLines ml
                INNER JOIN RentInvoices ri ON ri.Id = ml.InvoiceId
                LEFT JOIN MoveOutSettlementDeductions d ON d.SettlementId = ml.SettlementId AND d.InvoiceId = ml.InvoiceId
                WHERE ml.SettlementId = @Id
                ORDER BY CASE ml.LineType WHEN 'Outstanding' THEN 0 WHEN 'Deduction' THEN 1 ELSE 2 END, ri.DueDate, ml.InvoiceId;",
                new[] { new SqlParameter("@Id", id) });

        public async Task<DataTable> GetSettlementRefundsAsync(int id) =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT sd.Id AS DepositId, sd.EntryDate, -sd.Amount AS Amount, sd.PaymentMethod, sd.Reference, u.Username AS RecordedBy
                FROM SecurityDeposits sd LEFT JOIN Users u ON u.UserId = sd.CreatedBy
                WHERE sd.SettlementId = @Id AND sd.EntryType = 'Refund'
                ORDER BY sd.EntryDate, sd.Id;", new[] { new SqlParameter("@Id", id) });

        private static SqlParameter Money(string name, decimal value) =>
            new(name, SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = value };

        private static SqlParameter Text(string name, string? value, int size) =>
            new(name, SqlDbType.NVarChar, size) { Value = (object?)value ?? DBNull.Value };
    }
}
