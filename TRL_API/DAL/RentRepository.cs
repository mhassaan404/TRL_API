using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.Data;
using TRL_API.Models;

namespace TRL_API.DAL
{
    public class RentRepository : IRentRepository
    {
        private readonly DbHelper _dbHelper;

        public RentRepository(DbHelper dbHelper)
        {
            _dbHelper = dbHelper;
        }

        // Submitting multiple payments needs one open SqlConnection + SqlTransaction
        // shared across all the inserts/updates, so a failure partway through rolls
        // everything back instead of leaving half the payments recorded.
        public async Task<SqlConnection> GetOpenConnectionAsync() => await _dbHelper.GetOpenConnectionAsync();


        public async Task<DataTable> GetTenantsAsync()
        {
            string query = @"
                SELECT DISTINCT
                    t.TenantId,
                    CAST(t.TenantId AS VARCHAR(10)) + ' | ' + t.Name AS TenantName
                FROM Tenants t
                JOIN RentInvoices ri ON ri.TenantId = t.TenantId
                WHERE t.IsActive = 1
                  AND ri.StatusId IN (2, 8)
                ORDER BY t.TenantId;";

            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query);
        }

        public async Task<DataTable> GetInvoicesByTenantAsync(int tenantId)
        {
            // FIX: was selecting "ri.Status" which does not exist on RentInvoices
            // (only StatusId exists) — joined StatusList to get the real name instead.
            string query = @"
                SELECT
                    ri.Id AS InvoiceId,
                    ri.InvoiceDate,
                    ri.PendingAmount,
                    ri.TotalRent,
                    ri.ChargeType,
                    s.StatusName AS Status,
                    u.UnitNumber,
                    f.FloorNumber,
                    b.BuildingName
                FROM RentInvoices ri
                INNER JOIN Tenants t ON ri.TenantId = t.TenantId
                LEFT JOIN Units u ON u.UnitId = ISNULL(ri.UnitId, t.UnitId)
                LEFT JOIN Floors f ON u.FloorId = f.FloorId
                LEFT JOIN Buildings b ON f.BuildingId = b.BuildingId
                INNER JOIN StatusList s ON ri.StatusId = s.StatusId
                WHERE ri.TenantId = @TenantId
                ORDER BY ri.InvoiceDate DESC;";

            var parameters = new[] { new SqlParameter("@TenantId", tenantId) };
            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
        }

        //            p.PaymentDate,
        //            ri.TotalRent AS MonthlyRent,
        //            ISNULL(p.PaymentAmount, 0) AS PaidAmount,

        //            (ri.TotalRent - ISNULL(SUM(p.PaymentAmount + p.DiscountAmount)
        //                                    OVER (PARTITION BY ri.Id
        //                                          ORDER BY p.PaymentDate, p.Id
        //                                          ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW), 0))
        //                AS RemainingAmount,

        //            SUM(ISNULL(p.PaymentAmount, 0)) OVER (
        //                PARTITION BY ri.Id
        //                ORDER BY p.PaymentDate, p.Id
        //                ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
        //            ) AS TotalPaid,

        //            ISNULL(p.DiscountAmount, 0) AS DiscountAmount,
        //            ISNULL(p.DiscountPercent, 0) AS DiscountPercent,
        //            ISNULL(p.IsLateFeeWaived, 0) AS waveLateFee,
        //            ISNULL(p.PaymentMethod, '') AS PaymentMethod,
        //            ISNULL(p.Notes, '') AS Notes


        public async Task<DataTable> GetPaymentHistoryByIdAsync(int invoiceId)
        {
            string query = @"
                SELECT
                    p.PaymentDate,
                    ri.TotalRent AS MonthlyRent,
                    ISNULL(p.PaymentAmount, 0) AS PaidAmount,

                    (
                        (ri.TotalRent + ri.LateFeeCharged)
                        - ISNULL(
                            SUM(p.PaymentAmount + p.DiscountAmount)
                            OVER (
                                PARTITION BY ri.Id
                                ORDER BY p.PaymentDate, p.Id
                                ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
                            ),
                            0
                        )
                    ) AS RemainingAmount,

                    SUM(ISNULL(p.PaymentAmount, 0)) OVER (
                        PARTITION BY ri.Id
                        ORDER BY p.PaymentDate, p.Id
                        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
                    ) AS TotalPaid,

                    ISNULL(p.DiscountAmount, 0) AS DiscountAmount,
                    ISNULL(p.DiscountPercent, 0) AS DiscountPercent,
                    ISNULL(p.IsLateFeeWaived, 0) AS waveLateFee,
                    ISNULL(p.PaymentMethod, '') AS PaymentMethod,
                    ISNULL(p.Notes, '') AS Notes

                FROM Payments p
                INNER JOIN RentInvoices ri ON p.RentInvoiceId = ri.Id
                WHERE ri.Id = @InvoiceId
                ORDER BY p.PaymentDate DESC, p.Id DESC;";

            var parameters = new[] { new SqlParameter("@InvoiceId", invoiceId) };
            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
        }


        //        WITH PaymentSums AS (
        //            SELECT
        //                ri.Id AS InvoiceId,
        //                ISNULL(SUM(p.PaymentAmount), 0) AS Paid,
        //                ISNULL(SUM(p.DiscountAmount), 0) AS DiscountAmount,
        //                ISNULL(MAX(p.DiscountPercent), 0) AS DiscountPercent,
        //                MAX(CASE WHEN p.IsLateFeeWaived = 1 THEN 1 ELSE 0 END) AS WaveLate
        //                ri.Id AS InvoiceId,
        //                ri.TenantId,
        //                t.Name AS TenantName,
        //                ri.InvoiceDate,
        //                ri.DueDate,
        //                ri.TotalRent AS MonthlyRent,
        //                ps.Paid AS PaidAmount,
        //                    ELSE 0
        //                END AS LateFee,
        //                ps.WaveLate,
        //                ps.DiscountAmount,
        //                ps.DiscountPercent,
        //                0 AS PayAmount,
        //                NULL AS PaymentDate,
        //                NULL AS Method,
        //                '' AS Notes,
        //                SUM(ri.TotalRent - ISNULL(ps.Paid, 0) - ISNULL(ps.DiscountAmount, 0))
        //                    OVER(PARTITION BY ri.TenantId ORDER BY ri.DueDate
        //                         ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING) AS PreviousBalance,
        //                SUM(CASE WHEN ri.DueDate < GETDATE() AND ps.WaveLate = 0
        //                         THEN ROUND(ri.TotalRent * 0.05, 0) ELSE 0 END)
        //                    OVER(PARTITION BY ri.TenantId) AS TotalLateFeePerTenant,
        //                SUM(ps.Paid) OVER(PARTITION BY ri.TenantId) AS TotalPaidPerTenant


        public async Task<DataTable> GetUnpaidInvoiceByTenant(int tenantId)
        {
            // Balances and late fees come from dbo.InvoiceBalance (the one shared definition).
            string query = @"
                WITH Base AS (
                    SELECT ri.Id AS InvoiceId, ri.TenantId, t.Name AS TenantName, ri.InvoiceDate, ri.DueDate,
                           ri.TotalRent AS MonthlyRent, bal.Paid AS PaidAmount, bal.Disc AS DiscountAmount,
                           ISNULL(dp.DiscountPercent, 0) AS DiscountPercent, bal.Waived AS WaveLate, ri.LateFeeCharged,
                           bal.Balance AS RemainingAmount, bal.OpenLateFee AS LateFee
                    FROM RentInvoices ri
                    CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) bal
                    -- Reversal rows and reversed rows don't count
                    OUTER APPLY (SELECT MAX(dp1.DiscountPercent) AS DiscountPercent FROM Payments dp1
                                 WHERE dp1.RentInvoiceId = ri.Id AND dp1.ReversalOfPaymentId IS NULL
                                   AND NOT EXISTS (SELECT 1 FROM Payments dp2 WHERE dp2.ReversalOfPaymentId = dp1.Id)) dp
                    LEFT JOIN Tenants t ON ri.TenantId = t.TenantId
                    WHERE ri.StatusId IN (2, 3, 4, 8) AND ri.TenantId = @TenantId
                )
                SELECT b.*,
                       SUM(b.RemainingAmount) OVER (PARTITION BY b.TenantId ORDER BY b.DueDate
                            ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING) AS PreviousBalance,
                       SUM(b.LateFee) OVER (PARTITION BY b.TenantId) AS TotalLateFeePerTenant,
                       SUM(b.PaidAmount) OVER (PARTITION BY b.TenantId) AS TotalPaidPerTenant
                FROM Base b
                ORDER BY b.InvoiceDate DESC;";

            var parameters = new[] { new SqlParameter("@TenantId", tenantId) };
            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
        }


        // =========================
        // Payment writes
        // =========================

        // An adjustment is a reversal: a negative amount no larger than what has been paid on the invoice.
        // Locks the invoice row so two concurrent reversals can't both pass the check.
        public async Task<string?> ValidateAdjustmentAsync(Payments p, SqlConnection conn, SqlTransaction tx)
        {
            if (p.PaymentAmount >= 0) return "Adjustment amount must be a negative (reversal) amount.";
            if (string.IsNullOrWhiteSpace(p.Notes)) return "A reason is required for an adjustment.";

            const string q = @"
                SELECT ri.TenantId, ri.StatusId,
                       (SELECT ISNULL(SUM(PaymentAmount),0) FROM Payments WHERE RentInvoiceId = ri.Id) AS Paid
                FROM RentInvoices ri WITH (UPDLOCK, HOLDLOCK)
                WHERE ri.Id = @Id;";

            using var cmd = new SqlCommand(q, conn, tx);
            cmd.Parameters.AddWithValue("@Id", p.RentInvoiceId);
            using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return "Invoice not found.";

            if (Convert.ToInt32(r["TenantId"]) != p.TenantId) return "Invoice does not belong to this tenant.";
            if (Convert.ToInt32(r["StatusId"]) == 6) return "This invoice is cancelled.";

            decimal paid = Convert.ToDecimal(r["Paid"]);
            if (-p.PaymentAmount > paid + 0.005m)
                return $"Cannot reverse more than the {paid:N0} paid on invoice #{p.RentInvoiceId}.";
            return null;
        }

        public async Task<ApiResponse> CreatePaymentAdjustmentAsync(Payments payment, int userId, SqlConnection conn, SqlTransaction transaction)
        {
            string query = @"
                INSERT INTO Payments
                    (TenantId, PaymentAmount, PaymentDate, RentInvoiceId, PaymentMethod, Notes,
                        DiscountAmount, DiscountPercent, IsLateFeeWaived, CreatedBy, CreatedAt)
                VALUES
                    (@TenantId, @PaymentAmount, GETDATE(), @RentInvoiceId, @PaymentMethod, @Notes,
                        @DiscountAmount, @DiscountPercent, @IsLateFeeWaived, @CreatedBy, GETDATE());";

            var parameters = new[]
            {
                new SqlParameter("@TenantId", payment.TenantId),
                new SqlParameter("@PaymentAmount", payment.PaymentAmount),
                new SqlParameter("@RentInvoiceId", payment.RentInvoiceId),
                new SqlParameter("@PaymentMethod", string.IsNullOrWhiteSpace(payment.PaymentMethod) ? (object)DBNull.Value : payment.PaymentMethod),
                new SqlParameter("@Notes", string.IsNullOrWhiteSpace(payment.Notes) ? (object)DBNull.Value : payment.Notes),
                new SqlParameter("@DiscountAmount", payment.DiscountAmount),
                new SqlParameter("@DiscountPercent", payment.DiscountPercent),
                new SqlParameter("@IsLateFeeWaived", payment.IsLateFeeWaived),
                new SqlParameter("@CreatedBy", userId),
            };

            return await _dbHelper.ExecuteQueryAsync(query, parameters, conn, transaction);
        }

        public async Task<ApiResponse> CreateRentAsync(Payments payment, int userId, SqlConnection conn, SqlTransaction transaction)
        {
            string query = @"
                INSERT INTO Payments
                    (TenantId, PaymentAmount, PaymentDate, RentInvoiceId, PaymentMethod, Notes,
                        DiscountAmount, DiscountPercent, IsLateFeeWaived, CreatedBy, CreatedAt)
                VALUES
                    (@TenantId, @PaymentAmount, @PaymentDate, @RentInvoiceId, @PaymentMethod, @Notes,
                        @DiscountAmount, @DiscountPercent, @IsLateFeeWaived, @CreatedBy, GETDATE());";

            var parameters = new[]
            {
                new SqlParameter("@TenantId", payment.TenantId),
                new SqlParameter("@PaymentAmount", payment.PaymentAmount),
                new SqlParameter("@PaymentDate", payment.PaymentDate),
                new SqlParameter("@RentInvoiceId", payment.RentInvoiceId),
                // FIX: this was previously bound to payment.RentInvoiceId instead of
                // payment.PaymentMethod — every payment's method was being stored as
                // the invoice ID number instead of "Cash"/"Bank Transfer"/etc.
                new SqlParameter("@PaymentMethod", string.IsNullOrWhiteSpace(payment.PaymentMethod) ? (object)DBNull.Value : payment.PaymentMethod),
                new SqlParameter("@Notes", string.IsNullOrWhiteSpace(payment.Notes) ? (object)DBNull.Value : payment.Notes),
                new SqlParameter("@DiscountAmount", payment.DiscountAmount),
                new SqlParameter("@DiscountPercent", payment.DiscountPercent),
                new SqlParameter("@IsLateFeeWaived", payment.IsLateFeeWaived),
                new SqlParameter("@CreatedBy", userId),
            };

            return await _dbHelper.ExecuteQueryAsync(query, parameters, conn, transaction);
        }

        // The payment just inserted by CreateRentAsync in this transaction (its Id is the receipt number). Safe: the
        // invoice row is locked by ValidatePaymentAsync until the transaction ends, so no other payment can be added
        // to it in between.
        public async Task<int> GetLatestPaymentIdAsync(int invoiceId, int userId, SqlConnection conn, SqlTransaction transaction)
        {
            using var cmd = new SqlCommand(
                "SELECT MAX(Id) FROM Payments WHERE RentInvoiceId = @InvoiceId AND CreatedBy = @UserId;", conn, transaction);
            cmd.Parameters.AddWithValue("@InvoiceId", invoiceId);
            cmd.Parameters.AddWithValue("@UserId", userId);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        // Reverses one payment row as a whole: inserts a linked row with the same cash and discount as negatives (its
        // waiver stops counting in dbo.InvoiceBalance) and recalculates the invoice. The original row is not changed.
        // The invoice row is locked first (same order as payments, so they can't interleave), then every rule in
        // InvoiceSql.ReversalBlock is re-checked; UX_Payments_ReversalOf is the last guard against a double reversal.
        // Result: 'OK' (+ ReversalId, InvoiceId, Balance, Status) or a reason code from ReversalBlock / NOT_FOUND.
        public async Task<DataTable> ReversePaymentAsync(int paymentId, string reason, int userId)
        {
            string query = @"
                SET XACT_ABORT ON;
                DECLARE @InvoiceId INT, @Locked INT, @Block VARCHAR(20), @NewId INT;
                BEGIN TRAN;
                SELECT @InvoiceId = RentInvoiceId FROM Payments WHERE Id = @PaymentId;
                IF @InvoiceId IS NULL
                BEGIN ROLLBACK; SELECT 'NOT_FOUND' AS Result, CAST(NULL AS INT) AS ReversalId, CAST(NULL AS INT) AS InvoiceId,
                                       CAST(NULL AS DECIMAL(18,2)) AS Balance, CAST(NULL AS VARCHAR(50)) AS Status; RETURN; END

                SELECT @Locked = Id FROM RentInvoices WITH (UPDLOCK, HOLDLOCK) WHERE Id = @InvoiceId;

                SELECT @Block = " + InvoiceSql.ReversalBlock + @"
                FROM Payments p WITH (UPDLOCK, HOLDLOCK)
                INNER JOIN RentInvoices ri ON ri.Id = p.RentInvoiceId
                WHERE p.Id = @PaymentId;
                IF @Block IS NOT NULL
                BEGIN ROLLBACK; SELECT @Block AS Result, CAST(NULL AS INT) AS ReversalId, @InvoiceId AS InvoiceId,
                                       CAST(NULL AS DECIMAL(18,2)) AS Balance, CAST(NULL AS VARCHAR(50)) AS Status; RETURN; END

                INSERT INTO Payments
                    (TenantId, PaymentAmount, PaymentDate, RentInvoiceId, PaymentMethod, Notes,
                     DiscountAmount, DiscountPercent, IsLateFeeWaived, CreatedBy, CreatedAt, ReversalOfPaymentId)
                SELECT TenantId, -PaymentAmount, GETDATE(), RentInvoiceId, PaymentMethod, @Reason,
                       -DiscountAmount, 0, 0, @UserId, GETDATE(), Id
                FROM Payments WHERE Id = @PaymentId;
                SET @NewId = SCOPE_IDENTITY();
                " + RecalcInvoiceSql + @"
                COMMIT;

                SELECT 'OK' AS Result, @NewId AS ReversalId, @InvoiceId AS InvoiceId, b.Balance, s.StatusName AS Status
                FROM RentInvoices ri
                CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) b
                LEFT JOIN StatusList s ON s.StatusId = ri.StatusId
                WHERE ri.Id = @InvoiceId;";

            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, new[]
            {
                new SqlParameter("@PaymentId", paymentId),
                new SqlParameter("@Reason", SqlDbType.NVarChar, 500) { Value = reason },
                new SqlParameter("@UserId", userId),
            });
        }


        // Recalculates one invoice's pending/overpaid/status from its payments (cancelled invoices are left alone)
        private const string RecalcInvoiceSql = InvoiceSql.Recalc + @"
                WHERE ri.Id = @InvoiceId AND ri.StatusId <> 6;";

        public async Task<ApiResponse> RecalcInvoiceAsync(int invoiceId, SqlConnection conn, SqlTransaction transaction)
        {
            var parameters = new[] { new SqlParameter("@InvoiceId", invoiceId) };
            return await _dbHelper.ExecuteQueryAsync(RecalcInvoiceSql, parameters, conn, transaction);
        }


        // Server-side payment rules (inside the same transaction as the insert)
        public async Task<string?> ValidatePaymentAsync(Payments p, SqlConnection conn, SqlTransaction tx)
        {
            // The first statement locks the invoice row (UPDLOCK/HOLDLOCK) before anything reads it, so a second payment
            // on the same invoice waits until this transaction commits and then validates against the updated balance.
            // (Locking inside the main SELECT deadlocks: dbo.InvoiceBalance reads the row with a shared lock first.)
            // RecentDuplicate catches a double-submitted new payment (same invoice and amounts within 10 seconds)
            // that the balance check alone would let through.
            const string q = @"
                DECLARE @Locked INT;
                SELECT @Locked = Id FROM RentInvoices WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id;
                SELECT ri.TenantId, ri.StatusId, b.Balance, b.RentBalance, b.OpenLateFee AS OpenFee,
                    CASE WHEN EXISTS (
                        SELECT 1 FROM Payments p
                        WHERE p.RentInvoiceId = ri.Id AND p.PaymentAmount = @PaymentAmount
                          AND p.DiscountAmount = @DiscountAmount AND p.IsLateFeeWaived = @IsLateFeeWaived
                          AND p.CreatedAt >= DATEADD(SECOND, -10, GETDATE())) THEN 1 ELSE 0 END AS RecentDuplicate
                FROM RentInvoices ri
                CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) b
                WHERE ri.Id = @Id;";

            using var cmd = new SqlCommand(q, conn, tx);
            cmd.Parameters.AddWithValue("@Id", p.RentInvoiceId);
            cmd.Parameters.Add(new SqlParameter("@PaymentAmount", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = p.PaymentAmount });
            cmd.Parameters.Add(new SqlParameter("@DiscountAmount", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = p.DiscountAmount });
            cmd.Parameters.AddWithValue("@IsLateFeeWaived", p.IsLateFeeWaived);
            using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return "Invoice not found.";
            if (Convert.ToInt32(r["RecentDuplicate"]) == 1)
                return $"An identical payment for invoice #{p.RentInvoiceId} was just recorded. Refresh to see it before recording again.";

            if (Convert.ToInt32(r["TenantId"]) != p.TenantId) return "Invoice does not belong to this tenant.";
            if (Convert.ToInt32(r["StatusId"]) == 6) return "This invoice is cancelled.";

            decimal bal = Convert.ToDecimal(r["Balance"]);
            decimal rentBal = Convert.ToDecimal(r["RentBalance"]);
            decimal openFee = Convert.ToDecimal(r["OpenFee"]);
            decimal applied = p.PaymentAmount + p.DiscountAmount;

            if (applied > bal + 0.005m)
                return $"Amount exceeds the balance of {bal:N0} on invoice #{p.RentInvoiceId}.";
            if (openFee > 0 && !p.IsLateFeeWaived && rentBal > 0 && rentBal - applied <= 0)
                return $"Invoice #{p.RentInvoiceId} is overdue. Charge or waive the late fee before closing it.";
            return null;
        }

        public async Task<DataTable> ReverseLateFeeAsync(int invoiceId, string reason, int userId)
        {
            string query = @"
                DECLARE @Fee DECIMAL(18,2);
                -- Only while payments haven't eaten into the fee yet (rent balance still >= 0)
                SELECT @Fee = ri.LateFeeCharged
                FROM RentInvoices ri
                CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) b
                WHERE ri.Id = @InvoiceId AND ri.LateFeeCharged > 0 AND ri.StatusId <> 6 AND b.RentBalance >= 0;

                IF @Fee IS NULL
                BEGIN SELECT CAST(0 AS DECIMAL(18,2)) AS Fee; RETURN; END

                UPDATE RentInvoices SET LateFeeCharged = 0, LateFeeChargedAt = NULL WHERE Id = @InvoiceId;
                INSERT INTO InvoiceAudit(InvoiceId, Action, Amount, Reason, CreatedBy)
                VALUES (@InvoiceId, 'LATE_FEE_REVERSED', @Fee, @Reason, @UserId);"
                        + RecalcInvoiceSql + @"
                SELECT @Fee AS Fee;";

            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, new[]
            {
                new SqlParameter("@InvoiceId", invoiceId),
                new SqlParameter("@Reason", reason),
                new SqlParameter("@UserId", userId),
            });
        }

        public async Task<DataTable> GetOccupancyAsync() =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync(
                "SELECT * FROM vw_UnitOccupancy ORDER BY BuildingName, FloorNumber, UnitNumber;");

        public async Task<DataTable> GetVacantUnitsAsync(int? includeUnitId)
        {
            var prm = new[] { new SqlParameter("@Include", SqlDbType.Int) { Value = (object?)includeUnitId ?? DBNull.Value } };
            // Vacant = no active lease on the unit (leases are the source of truth, so every unit of a tenant with
            // several leases is excluded, not just the one the Tenants row points at). Deleted units, floors and
            // buildings are left out. The lease form builds its building and floor lists from these rows, so a
            // floor only appears when it has at least one vacant unit.
            return await _dbHelper.ExecuteQueryReturnDataTableAsync(
                @"SELECT b.BuildingId, b.BuildingName, f.FloorId, f.FloorNumber, u.UnitId, u.UnitNumber, u.BaseRent AS UnitRent
                    FROM Units u
                    JOIN Floors f ON f.FloorId = u.FloorId
                    JOIN Buildings b ON b.BuildingId = f.BuildingId
                    WHERE u.IsActive = 1 AND f.IsActive = 1 AND b.IsActive = 1
                      AND (NOT EXISTS (SELECT 1 FROM TenantLeases tl WHERE tl.UnitId = u.UnitId AND tl.IsActive = 1)
                           OR u.UnitId = @Include)
                    ORDER BY b.BuildingName, f.FloorNumber, u.UnitNumber;", prm);
        }


        public async Task<DataTable> GetRentCollectionAsync()
        {
            // Balances and late fees come from dbo.InvoiceBalance (the one shared definition).
            string query = @"
                SELECT
                    ri.Id AS InvoiceId,
                    ri.LeaseId,
                    t.TenantId,
                    t.Name AS TenantName,
                    bd.BuildingName,
                    f.FloorNumber,
                    u.UnitNumber,
                    ri.InvoiceDate,
                    ri.LateFeeCharged,
                    ISNULL(ri.TotalRent, 0) AS MonthlyRent,
                    ri.DueDate,
                    bal.Balance AS RemainingAmount,
                    bal.OpenLateFee AS LateFee,
                    s.StatusName,
                    bal.Paid AS PaidAmount,
                    bal.Disc AS AppliedDiscount,
                    bal.LastPaymentDate,
                    ri.Description,
                    ri.ChargeType,
                    (
                        SELECT ISNULL(SUM(b2.Balance), 0)
                        FROM RentInvoices ri2
                        CROSS APPLY dbo.InvoiceBalance(ri2.Id, 0) b2
                        WHERE ri2.TenantId = t.TenantId AND ri2.Id <> ri.Id AND ri2.StatusId IN (2,8,9)
                    ) AS PreviousBalance
                FROM RentInvoices ri
                INNER JOIN Tenants t ON ri.TenantId = t.TenantId
                LEFT JOIN Units u ON u.UnitId = ISNULL(ri.UnitId, t.UnitId)
                LEFT JOIN Floors f ON u.FloorId = f.FloorId
                LEFT JOIN Buildings bd ON f.BuildingId = bd.BuildingId
                INNER JOIN StatusList s ON ri.StatusId = s.StatusId
                CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) bal
                WHERE ri.StatusId IN (2, 8, 9)
                ORDER BY ri.InvoiceDate DESC, t.Name;";

            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query);
        }

        public async Task<DataTable> GetTenantsWithRent()
        {
            string query = @"SELECT TenantId, Name, MonthlyRent FROM Tenants WHERE IsActive = 1 ORDER BY Name;";
            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query);
        }

        public async Task<DataTable> GetPaymentHistoryAsync(int invoiceId)
        {
            // FIX: was selecting "StatusId" from Payments — that column doesn't
            // exist on the Payments table (only on RentInvoices).
            string query = @"
                SELECT PaymentAmount, PaymentDate, PaymentMethod
                FROM Payments
                WHERE RentInvoiceId = @InvoiceId
                ORDER BY PaymentDate ASC;";

            var parameters = new[] { new SqlParameter("@InvoiceId", invoiceId) };
            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
        }

        public async Task<ApiResponse> BulkUpdateDueDateAsync(List<int> invoiceIds, DateTime newDueDate)
        {
            var inParams = invoiceIds.Select((id, index) => $"@Id{index}").ToArray();
            string query = $@"
                UPDATE RentInvoices
                SET DueDate = @NewDueDate
                WHERE Id IN ({string.Join(", ", inParams)});";

            var parameters = invoiceIds
                .Select((id, index) => new SqlParameter($"@Id{index}", id))
                .ToList();
            parameters.Add(new SqlParameter("@NewDueDate", newDueDate));

            return await _dbHelper.ExecuteQueryAsync(query, parameters.ToArray());
        }

        // =========================
        // Bulk invoice generation (manual button — no scheduled service)
        // =========================

        // For populating the tenant multiselect in both the Generate Invoices
        // and Add Extra Charge modals — every active tenant, not filtered by
        // whether they already have an invoice (that filtering happens at
        // generation time so re-running is always safe).
        public async Task<DataTable> GetActiveTenantsList()
        {
            string query = @"
                SELECT t.TenantId, t.Name
                FROM Tenants t
                WHERE t.IsActive = 1 AND t.MoveOutDate IS NULL
                ORDER BY t.Name;";

            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query);
        }

        // Names of all non-deleted tenants (active or moved out), for messages that list tenants by name
        public async Task<DataTable> GetTenantNamesAsync()
        {
            return await _dbHelper.ExecuteQueryReturnDataTableAsync(
                "SELECT TenantId, Name FROM Tenants WHERE IsDeleted = 0;");
        }

        public async Task<DataTable> GetLeaseChargesForMonth(int month, int year)
        {
            string query = @"
                DECLARE @MonthStart DATE = DATEFROMPARTS(@Year, @Month, 1);
                -- Every lease that covers part of the month, flagged when that month's rent is already invoiced.
                -- Cancelled invoices (StatusId 6) don't count, the same rule as the unique index
                -- UX_RentInvoices_RentPerLeaseMonth: a cancelled month can be generated again.
                SELECT l.LeaseId, l.TenantId, l.UnitId, c.Amount, c.FromDate, c.Descr,
                    CAST(CASE WHEN EXISTS (
                                SELECT 1 FROM RentInvoices ri
                                WHERE ri.LeaseId = l.LeaseId AND ri.InvoiceMonth = @MonthStart
                                  AND ri.ChargeType IS NULL AND ri.StatusId <> 6)
                              -- invoices from before leases were linked to invoices (LeaseId NULL) for the same tenant+unit+month
                              OR EXISTS (
                                SELECT 1 FROM RentInvoices ri
                                WHERE ri.LeaseId IS NULL AND ri.TenantId = l.TenantId AND ri.UnitId = l.UnitId
                                  AND ri.InvoiceMonth = @MonthStart AND ri.ChargeType IS NULL AND ri.StatusId <> 6)
                         THEN 1 ELSE 0 END AS BIT) AS AlreadyInvoiced
                FROM TenantLeases l
                JOIN Tenants t ON t.TenantId = l.TenantId
                CROSS APPLY dbo.LeaseMonthCharge(l.LeaseId, @MonthStart) c
                WHERE t.IsDeleted = 0 AND c.Days > 0 AND c.Amount > 0;";
            var parameters = new[] { new SqlParameter("@Month", month), new SqlParameter("@Year", year) };
            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
        }

        // description/chargeType are null for a regular monthly rent invoice,
        // and populated for a one-time extra charge (Maintenance, Late Fine, etc.)
        // leaseId/unitId are set for monthly rent (billed from a lease); extra charges leave them null and
        // the unit defaults to the tenant's current unit so the invoice still shows where it belongs.
        public async Task<ApiResponse> CreateInvoice(
            int tenantId, decimal totalRent, DateTime invoiceDate, DateTime dueDate, decimal lateFeePerDay, decimal lateFeeMaxMultiplier,
            string? description = null, string? chargeType = null, int? leaseId = null, int? unitId = null)
        {
            // StatusId 2 = Unpaid. NOT 3 (Pending): GetRentCollectionAsync only
            // shows StatusId IN (2,8,9), so a Pending invoice would never surface
            // in the collections list until something else changed its status —
            // and since there's no scheduled job, it would sit invisible forever.
            // LateFeePerDay / LateFeeMaxMultiplier: the late-fee rule in force when the invoice is created, kept with
            // the invoice so later settings changes don't re-price it.
            string query = @"
                INSERT INTO RentInvoices
                    (TenantId, LeaseId, UnitId, TotalRent, PendingAmount, OverPaidAmount, InvoiceDate, DueDate, StatusId, Description, ChargeType, CreatedAt,
                     LateFeePerDay, LateFeeMaxMultiplier)
                VALUES
                    (@TenantId, @LeaseId, ISNULL(@UnitId, (SELECT UnitId FROM Tenants WHERE TenantId = @TenantId)),
                     @TotalRent, @TotalRent, 0, @InvoiceDate, @DueDate, 2, @Description, @ChargeType, GETDATE(),
                     @LateFeePerDay, @LateFeeMaxMultiplier);";

            var parameters = new[]
            {
                new SqlParameter("@TenantId", tenantId),
                new SqlParameter("@TotalRent", totalRent),
                new SqlParameter("@InvoiceDate", invoiceDate),
                new SqlParameter("@DueDate", dueDate),
                new SqlParameter("@Description", (object?)description ?? DBNull.Value),
                new SqlParameter("@ChargeType", (object?)chargeType ?? DBNull.Value),
                new SqlParameter("@LeaseId", (object?)leaseId ?? DBNull.Value),
                new SqlParameter("@UnitId", (object?)unitId ?? DBNull.Value),
                new SqlParameter("@LateFeePerDay", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = lateFeePerDay },
                new SqlParameter("@LateFeeMaxMultiplier", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = lateFeeMaxMultiplier },
            };

            return await _dbHelper.ExecuteQueryAsync(query, parameters);
        }

        // Extra charges: one new invoice per tenant, all in one transaction (all or nothing). Creation is serialized
        // (app lock), so a double submit finds the first one and is refused as a duplicate (same tenant, type, amount,
        // description, charge date and related invoice within the last minute). Tenants must exist (not deleted);
        // the related invoice must belong to the tenant. No existing invoice or payment is changed.
        // Result: one row per created invoice ('OK', Info = invoice id), or one row with the reason it was refused.
        public async Task<DataTable> CreateExtraChargesAsync(IReadOnlyList<int> tenantIds, DateTime chargeDate, DateTime dueDate,
            string chargeType, string? description, decimal amount, int? relatedInvoiceId, decimal lateFeePerDay, decimal lateFeeMaxMultiplier)
        {
            var tenantValues = string.Join(", ", tenantIds.Select((_, i) => $"(@T{i})"));
            string query = $@"
                SET XACT_ABORT ON;
                DECLARE @T TABLE (TenantId INT PRIMARY KEY);
                INSERT INTO @T (TenantId) VALUES {tenantValues};
                DECLARE @New TABLE (Id INT);

                BEGIN TRAN;
                DECLARE @Lock INT;
                EXEC @Lock = sp_getapplock @Resource = 'TRL_CreateExtraCharge', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
                IF @Lock < 0 BEGIN ROLLBACK; SELECT 'BUSY' AS Result, CAST(NULL AS INT) AS Info; RETURN; END

                IF EXISTS (SELECT 1 FROM @T x WHERE NOT EXISTS (SELECT 1 FROM Tenants t WHERE t.TenantId = x.TenantId AND t.IsDeleted = 0))
                BEGIN ROLLBACK; SELECT 'BAD_TENANT' AS Result, CAST(NULL AS INT) AS Info; RETURN; END

                IF @RelatedInvoiceId IS NOT NULL AND NOT EXISTS (
                    SELECT 1 FROM RentInvoices ri JOIN @T x ON x.TenantId = ri.TenantId WHERE ri.Id = @RelatedInvoiceId)
                BEGIN ROLLBACK; SELECT 'BAD_RELATED' AS Result, CAST(NULL AS INT) AS Info; RETURN; END

                IF EXISTS (SELECT 1 FROM RentInvoices ri JOIN @T x ON x.TenantId = ri.TenantId
                           WHERE ri.ChargeType = @ChargeType AND ri.TotalRent = @Amount AND ri.InvoiceDate = @ChargeDate
                             AND ISNULL(ri.Description, N'') = ISNULL(@Description, N'')
                             AND ISNULL(ri.RelatedInvoiceId, 0) = ISNULL(@RelatedInvoiceId, 0)
                             AND ri.StatusId <> 6 AND ri.CreatedAt >= DATEADD(SECOND, -60, GETDATE()))
                BEGIN ROLLBACK; SELECT 'DUPLICATE' AS Result, CAST(NULL AS INT) AS Info; RETURN; END

                -- Unit: the related invoice's unit, otherwise the tenant's current unit. Never linked to a lease, so lease
                -- billing (which only looks at ChargeType IS NULL) is unaffected. StatusId 2 = Unpaid.
                INSERT INTO RentInvoices
                    (TenantId, LeaseId, UnitId, TotalRent, PendingAmount, OverPaidAmount, InvoiceDate, DueDate, StatusId,
                     Description, ChargeType, CreatedAt, LateFeePerDay, LateFeeMaxMultiplier, RelatedInvoiceId)
                OUTPUT inserted.Id INTO @New
                SELECT x.TenantId, NULL, COALESCE((SELECT UnitId FROM RentInvoices WHERE Id = @RelatedInvoiceId), t.UnitId),
                       @Amount, @Amount, 0, @ChargeDate, @DueDate, 2,
                       @Description, @ChargeType, GETDATE(), @LateFeePerDay, @LateFeeMaxMultiplier, @RelatedInvoiceId
                FROM @T x JOIN Tenants t ON t.TenantId = x.TenantId;

                COMMIT;
                SELECT 'OK' AS Result, Id AS Info FROM @New ORDER BY Id;";

            var parameters = tenantIds.Select((id, i) => new SqlParameter($"@T{i}", id)).ToList();
            parameters.AddRange(new[]
            {
                new SqlParameter("@ChargeDate", SqlDbType.Date) { Value = chargeDate.Date },
                new SqlParameter("@DueDate", SqlDbType.Date) { Value = dueDate.Date },
                new SqlParameter("@ChargeType", SqlDbType.NVarChar, 50) { Value = chargeType },
                new SqlParameter("@Description", SqlDbType.NVarChar, 255) { Value = (object?)description ?? DBNull.Value },
                new SqlParameter("@Amount", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = amount },
                new SqlParameter("@RelatedInvoiceId", SqlDbType.Int) { Value = (object?)relatedInvoiceId ?? DBNull.Value },
                new SqlParameter("@LateFeePerDay", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = lateFeePerDay },
                new SqlParameter("@LateFeeMaxMultiplier", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = lateFeeMaxMultiplier },
            });
            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters.ToArray());
        }


        //Newly Added
        public async Task<DataTable> ChargeLateFeeAsync(int invoiceId)
        {
            string query = @"
                DECLARE @Fee DECIMAL(18,2);
                -- The fee that applies now (dbo.InvoiceBalance: overdue, rent still owed, not waived, not yet charged).
                -- Cancelled invoices are never charged.
                SELECT @Fee = b.OpenLateFee
                FROM RentInvoices ri
                CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) b
                WHERE ri.Id = @InvoiceId AND ri.StatusId <> 6;

                IF ISNULL(@Fee,0) <= 0
                BEGIN
                    SELECT CAST(0 AS DECIMAL(18,2)) AS Fee;
                    RETURN;
                END

                DECLARE @Charged INT;
                UPDATE RentInvoices
                SET LateFeeCharged = @Fee, LateFeeChargedAt = GETDATE()
                WHERE Id = @InvoiceId AND LateFeeCharged = 0;
                SET @Charged = @@ROWCOUNT;"
                        // Same pending/overpaid/status formula as payments and reversals
                        + RecalcInvoiceSql + @"
                SELECT CASE WHEN @Charged = 1 THEN @Fee ELSE CAST(0 AS DECIMAL(18,2)) END AS Fee;";

            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, new[] { new SqlParameter("@InvoiceId", invoiceId) });
        }

        public async Task<DataTable> GetAllPaymentsAsync(DateTime? from, DateTime? to)
        {
            string query = @"
                SELECT p.Id AS PaymentId, p.RentInvoiceId AS InvoiceId, t.TenantId, t.Name AS TenantName,
                       bd.BuildingName, f.FloorNumber, u.UnitNumber, ri.ChargeType, ri.InvoiceDate, p.PaymentDate, p.PaymentAmount,
                       p.DiscountAmount, p.PaymentMethod, p.Notes, p.IsLateFeeWaived, p.CreatedAt,
                       p.ReversalOfPaymentId, rv.Id AS ReversedByPaymentId, rv.CreatedAt AS ReversedAt, rv.Notes AS ReversalReason
                FROM Payments p
                LEFT JOIN Payments rv ON rv.ReversalOfPaymentId = p.Id
                INNER JOIN RentInvoices ri ON ri.Id = p.RentInvoiceId
                INNER JOIN Tenants t ON t.TenantId = p.TenantId
                LEFT JOIN Units u ON u.UnitId = ISNULL(ri.UnitId, t.UnitId)
                LEFT JOIN Floors f ON u.FloorId = f.FloorId
                LEFT JOIN Buildings bd ON f.BuildingId = bd.BuildingId
                WHERE (@From IS NULL OR p.PaymentDate >= @From)
                  AND (@To IS NULL OR p.PaymentDate < DATEADD(DAY, 1, @To))
                ORDER BY p.PaymentDate DESC, p.Id DESC;";

            var parameters = new[]
            {
                new SqlParameter("@From", SqlDbType.DateTime) { Value = (object?)from ?? DBNull.Value },
                new SqlParameter("@To", SqlDbType.DateTime) { Value = (object?)to ?? DBNull.Value },
            };
            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
        }
    }
}