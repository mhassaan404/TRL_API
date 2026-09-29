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
                    OUTER APPLY (SELECT MAX(DiscountPercent) AS DiscountPercent FROM Payments WHERE RentInvoiceId = ri.Id) dp
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

        // For an edit: the payment's current cash amount and the cash of every other payment on the invoice
        // (null if the payment isn't on that invoice).
        public async Task<(decimal Current, decimal Others)?> GetPaymentEditInfoAsync(int paymentId, int invoiceId, SqlConnection conn, SqlTransaction tx)
        {
            const string q = @"
                SELECT p.PaymentAmount,
                       (SELECT ISNULL(SUM(o.PaymentAmount), 0) FROM Payments o WHERE o.RentInvoiceId = p.RentInvoiceId AND o.Id <> p.Id) AS Others
                FROM Payments p WITH (UPDLOCK)
                WHERE p.Id = @Id AND p.RentInvoiceId = @InvoiceId;";
            using var cmd = new SqlCommand(q, conn, tx);
            cmd.Parameters.AddWithValue("@Id", paymentId);
            cmd.Parameters.AddWithValue("@InvoiceId", invoiceId);
            using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            return (Convert.ToDecimal(r["PaymentAmount"]), Convert.ToDecimal(r["Others"]));
        }

        // Edits an existing payment in place. The payment must already belong to the given invoice and tenant.
        public async Task<ApiResponse> UpdatePaymentAsync(Payments payment, int userId, SqlConnection conn, SqlTransaction transaction)
        {
            string query = @"
                UPDATE Payments SET
                    PaymentAmount = @PaymentAmount, PaymentDate = @PaymentDate, PaymentMethod = @PaymentMethod,
                    Notes = @Notes, DiscountAmount = @DiscountAmount, DiscountPercent = @DiscountPercent,
                    IsLateFeeWaived = @IsLateFeeWaived, UpdatedBy = @UpdatedBy, UpdatedAt = GETDATE()
                WHERE Id = @Id AND RentInvoiceId = @RentInvoiceId AND TenantId = @TenantId;";

            var parameters = new[]
            {
                new SqlParameter("@Id", payment.Id),
                new SqlParameter("@TenantId", payment.TenantId),
                new SqlParameter("@RentInvoiceId", payment.RentInvoiceId),
                new SqlParameter("@PaymentAmount", payment.PaymentAmount),
                new SqlParameter("@PaymentDate", payment.PaymentDate),
                new SqlParameter("@PaymentMethod", string.IsNullOrWhiteSpace(payment.PaymentMethod) ? (object)DBNull.Value : payment.PaymentMethod),
                new SqlParameter("@Notes", string.IsNullOrWhiteSpace(payment.Notes) ? (object)DBNull.Value : payment.Notes),
                new SqlParameter("@DiscountAmount", payment.DiscountAmount),
                new SqlParameter("@DiscountPercent", payment.DiscountPercent),
                new SqlParameter("@IsLateFeeWaived", payment.IsLateFeeWaived),
                new SqlParameter("@UpdatedBy", userId),
            };

            return await _dbHelper.ExecuteQueryAsync(query, parameters, conn, transaction);
        }


        // Recalculates one invoice's pending/overpaid/status from its payments (cancelled invoices are left alone)
        private const string RecalcInvoiceSql = InvoiceSql.Recalc + @"
                WHERE ri.Id = @InvoiceId AND ri.StatusId <> 6;";

        public async Task<ApiResponse> RecalcInvoiceAsync(int invoiceId, SqlConnection conn, SqlTransaction transaction)
        {
            var parameters = new[] { new SqlParameter("@InvoiceId", invoiceId) };
            return await _dbHelper.ExecuteQueryAsync(RecalcInvoiceSql, parameters, conn, transaction);
        }


        public async Task<ApiResponse> DeleteLastPaymentForInvoice(int invoiceId)
        {
            string query = @"
                DELETE FROM Payments WHERE Id = (
                    SELECT TOP 1 Id FROM Payments WHERE RentInvoiceId = @InvoiceId 
                    ORDER BY CreatedAt DESC, Id DESC);" + RecalcInvoiceSql;
            return await _dbHelper.ExecuteQueryAsync(query, new[] { new SqlParameter("@InvoiceId", invoiceId) });
        }

        // Server-side payment rules (inside the same transaction as the insert)
        // excludePaymentId: when editing a payment, leave its current amounts out of the balance so they aren't counted twice.
        public async Task<string?> ValidatePaymentAsync(Payments p, SqlConnection conn, SqlTransaction tx, int excludePaymentId = 0)
        {
            const string q = @"
                SELECT ri.TenantId, ri.StatusId, b.Balance, b.RentBalance, b.OpenLateFee AS OpenFee
                FROM RentInvoices ri
                CROSS APPLY dbo.InvoiceBalance(ri.Id, @ExcludeId) b
                WHERE ri.Id = @Id;";

            using var cmd = new SqlCommand(q, conn, tx);
            cmd.Parameters.AddWithValue("@Id", p.RentInvoiceId);
            cmd.Parameters.AddWithValue("@ExcludeId", excludePaymentId);
            using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return "Invoice not found.";

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
            return await _dbHelper.ExecuteQueryReturnDataTableAsync(
                @"SELECT BuildingId, BuildingName, FloorId, FloorNumber, UnitId, UnitNumber, UnitRent
                    FROM vw_UnitOccupancy
                    WHERE Occupancy = 'Vacant' OR UnitId = @Include
                    ORDER BY BuildingName, FloorNumber, UnitNumber;", prm);
        }


        public async Task<DataTable> GetRentCollectionAsync()
        {
            // Balances and late fees come from dbo.InvoiceBalance (the one shared definition).
            string query = @"
                SELECT
                    ri.Id AS InvoiceId,
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

        public async Task<DataTable> GetLeaseChargesForMonth(int month, int year)
        {
            string query = @"
                DECLARE @MonthStart DATE = DATEFROMPARTS(@Year, @Month, 1);
                SELECT l.LeaseId, l.TenantId, l.UnitId, c.Amount, c.FromDate, c.Descr
                FROM TenantLeases l
                JOIN Tenants t ON t.TenantId = l.TenantId
                CROSS APPLY dbo.LeaseMonthCharge(l.LeaseId, @MonthStart) c
                WHERE t.IsDeleted = 0 AND c.Days > 0 AND c.Amount > 0
                  AND NOT EXISTS (
                        SELECT 1 FROM RentInvoices ri
                        WHERE ri.LeaseId = l.LeaseId AND ri.InvoiceMonth = @MonthStart AND ri.ChargeType IS NULL
                  )
                  -- invoices from before leases were linked to invoices (LeaseId NULL) for the same tenant+unit+month
                  AND NOT EXISTS (
                        SELECT 1 FROM RentInvoices ri
                        WHERE ri.LeaseId IS NULL AND ri.TenantId = l.TenantId AND ri.UnitId = l.UnitId
                          AND ri.InvoiceMonth = @MonthStart AND ri.ChargeType IS NULL AND ri.StatusId <> 6
                  );";
            var parameters = new[] { new SqlParameter("@Month", month), new SqlParameter("@Year", year) };
            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
        }

        // description/chargeType are null for a regular monthly rent invoice,
        // and populated for a one-time extra charge (Maintenance, Late Fine, etc.)
        // leaseId/unitId are set for monthly rent (billed from a lease); extra charges leave them null and
        // the unit defaults to the tenant's current unit so the invoice still shows where it belongs.
        public async Task<ApiResponse> CreateInvoice(
            int tenantId, decimal totalRent, DateTime invoiceDate, DateTime dueDate,
            string? description = null, string? chargeType = null, int? leaseId = null, int? unitId = null)
        {
            // StatusId 2 = Unpaid. NOT 3 (Pending): GetRentCollectionAsync only
            // shows StatusId IN (2,8,9), so a Pending invoice would never surface
            // in the collections list until something else changed its status —
            // and since there's no scheduled job, it would sit invisible forever.
            string query = @"
                INSERT INTO RentInvoices
                    (TenantId, LeaseId, UnitId, TotalRent, PendingAmount, OverPaidAmount, InvoiceDate, DueDate, StatusId, Description, ChargeType, CreatedAt)
                VALUES
                    (@TenantId, @LeaseId, ISNULL(@UnitId, (SELECT UnitId FROM Tenants WHERE TenantId = @TenantId)),
                     @TotalRent, @TotalRent, 0, @InvoiceDate, @DueDate, 2, @Description, @ChargeType, GETDATE());";

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
            };

            return await _dbHelper.ExecuteQueryAsync(query, parameters);
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
                       u.UnitNumber, ri.ChargeType, ri.InvoiceDate, p.PaymentDate, p.PaymentAmount,
                       p.DiscountAmount, p.PaymentMethod, p.Notes, p.IsLateFeeWaived, p.CreatedAt
                FROM Payments p
                INNER JOIN RentInvoices ri ON ri.Id = p.RentInvoiceId
                INNER JOIN Tenants t ON t.TenantId = p.TenantId
                LEFT JOIN Units u ON u.UnitId = ISNULL(ri.UnitId, t.UnitId)
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