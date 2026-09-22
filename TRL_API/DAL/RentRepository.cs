//using Microsoft.Data.SqlClient;
//using System.Data;
//using TRL_API.Data;
//using TRL_API.Models;

//namespace TRL_API.DAL
//{
//    public class RentRepository
//    {
//        private readonly DbHelper _dbHelper;

//        public RentRepository(DbHelper dbHelper)
//        {
//            _dbHelper = dbHelper;
//        }

//        // Delegates to DbHelper — add GetOpenConnectionAsync() there (see chat).
//        // Submitting multiple payments needs one open SqlConnection + SqlTransaction
//        // shared across all the inserts/updates, so a failure partway through rolls
//        // everything back instead of leaving half the payments recorded.
//        public async Task<SqlConnection> GetOpenConnectionAsync() => await _dbHelper.GetOpenConnectionAsync();

//        // =========================
//        // Data Retrieval Methods
//        // =========================

//        public async Task<DataTable> GetTenantsAsync()
//        {
//            string query = @"
//                SELECT DISTINCT
//                    t.TenantId,
//                    CAST(t.TenantId AS VARCHAR(10)) + ' | ' + t.Name AS TenantName
//                FROM Tenants t
//                JOIN RentInvoices ri ON ri.TenantId = t.TenantId
//                WHERE t.IsActive = 1
//                  AND ri.StatusId IN (2, 8)
//                ORDER BY t.TenantId;";

//            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query);
//        }

//        public async Task<DataTable> GetStatusListAsync()
//        {
//            string query = @"
//                SELECT StatusId, StatusName
//                FROM StatusList
//                WHERE IsActive = 1;";

//            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query);
//        }

//        public async Task<DataTable> GetInvoicesByTenantAsync(int tenantId)
//        {
//            // FIX: was selecting "ri.Status" which does not exist on RentInvoices
//            // (only StatusId exists) — joined StatusList to get the real name instead.
//            string query = @"
//                SELECT
//                    ri.Id AS InvoiceId,
//                    ri.InvoiceDate,
//                    ri.PendingAmount,
//                    s.StatusName AS Status,
//                    u.UnitNumber,
//                    f.FloorNumber,
//                    b.BuildingName
//                FROM RentInvoices ri
//                INNER JOIN Tenants t ON ri.TenantId = t.TenantId
//                INNER JOIN Units u ON t.UnitId = u.UnitId
//                INNER JOIN Floors f ON u.FloorId = f.FloorId
//                INNER JOIN Buildings b ON f.BuildingId = b.BuildingId
//                INNER JOIN StatusList s ON ri.StatusId = s.StatusId
//                WHERE ri.TenantId = @TenantId
//                ORDER BY ri.InvoiceDate DESC;";

//            var parameters = new[] { new SqlParameter("@TenantId", tenantId) };
//            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
//        }

//        public async Task<DataTable> GetInvoiceByIdAsync(int invoiceId)
//        {
//            // FIX: was joining Buildings ON u.BuildingId (Units has no BuildingId
//            // column) — now correctly goes through Floors, same as GetInvoicesByTenantAsync.
//            string query = @"
//                SELECT
//                    ri.Id AS InvoiceId,
//                    p.Id AS PaymentId,
//                    ri.TenantId,
//                    t.Name AS TenantName,
//                    b.BuildingName,
//                    f.FloorNumber,
//                    u.UnitNumber,
//                    ri.InvoiceDate,
//                    ri.TotalRent AS MonthlyRent,
//                    ri.DueDate,

//                    (ri.TotalRent - ISNULL(SUM(p.PaymentAmount) OVER (PARTITION BY ri.Id), 0)
//                                   - ISNULL(SUM(p.DiscountAmount) OVER (PARTITION BY ri.Id), 0)) AS RemainingAmount,

//                    ri.StatusId,
//                    s.StatusName,
//                    ISNULL(p.PaymentAmount, 0) AS PaidAmount,
//                    p.PaymentDate,
//                    p.PaymentMethod,
//                    p.Notes,
//                    p.CreatedAt,
//                    p.DiscountAmount,
//                    p.DiscountPercent,
//                    p.IsLateFeeWaived
//                FROM RentInvoices ri
//                INNER JOIN Tenants t ON ri.TenantId = t.TenantId
//                INNER JOIN Units u ON t.UnitId = u.UnitId
//                INNER JOIN Floors f ON u.FloorId = f.FloorId
//                INNER JOIN Buildings b ON f.BuildingId = b.BuildingId
//                INNER JOIN StatusList s ON ri.StatusId = s.StatusId
//                LEFT JOIN Payments p ON ri.Id = p.RentInvoiceId
//                WHERE ri.Id = @InvoiceId
//                ORDER BY p.PaymentDate DESC;";

//            var parameters = new[] { new SqlParameter("@InvoiceId", invoiceId) };
//            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
//        }

//        public async Task<DataTable> GetPaymentHistoryByIdAsync(int invoiceId)
//        {
//            string query = @"
//                SELECT
//                    p.PaymentDate,
//                    ri.TotalRent AS MonthlyRent,
//                    ISNULL(p.PaymentAmount, 0) AS PaidAmount,

//                    (ri.TotalRent - ISNULL(SUM(p.PaymentAmount + p.DiscountAmount)
//                                            OVER (PARTITION BY ri.Id
//                                                  ORDER BY p.PaymentDate, p.Id
//                                                  ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW), 0))
//                        AS RemainingAmount,

//                    SUM(ISNULL(p.PaymentAmount, 0)) OVER (
//                        PARTITION BY ri.Id
//                        ORDER BY p.PaymentDate, p.Id
//                        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
//                    ) AS TotalPaid,

//                    ISNULL(p.DiscountAmount, 0) AS DiscountAmount,
//                    ISNULL(p.DiscountPercent, 0) AS DiscountPercent,
//                    ISNULL(p.IsLateFeeWaived, 0) AS waveLateFee,
//                    ISNULL(p.PaymentMethod, '') AS PaymentMethod,
//                    ISNULL(p.Notes, '') AS Notes
//                FROM Payments p
//                INNER JOIN RentInvoices ri ON p.RentInvoiceId = ri.Id
//                WHERE ri.Id = @InvoiceId
//                ORDER BY p.PaymentDate DESC, p.Id DESC;";

//            var parameters = new[] { new SqlParameter("@InvoiceId", invoiceId) };
//            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
//        }

//        public async Task<DataTable> GetUnpaidInvoiceByTenant(int tenantId)
//        {
//            string query = @"
//                WITH PaymentSums AS (
//                    SELECT
//                        ri.Id AS InvoiceId,
//                        ISNULL(SUM(p.PaymentAmount), 0) AS Paid,
//                        ISNULL(SUM(p.DiscountAmount), 0) AS DiscountAmount,
//                        ISNULL(MAX(p.DiscountPercent), 0) AS DiscountPercent,
//                        MAX(CASE WHEN p.IsLateFeeWaived = 1 THEN 1 ELSE 0 END) AS WaveLate
//                    FROM RentInvoices ri
//                    LEFT JOIN Payments p ON ri.Id = p.RentInvoiceId
//                    GROUP BY ri.Id
//                ),
//                InvoiceWithTotals AS (
//                    SELECT
//                        ri.Id AS InvoiceId,
//                        ri.TenantId,
//                        t.Name AS TenantName,
//                        ri.InvoiceDate,
//                        ri.DueDate,
//                        ri.TotalRent AS MonthlyRent,
//                        ps.Paid AS PaidAmount,
//                        (ri.TotalRent - ISNULL(ps.Paid, 0) - ISNULL(ps.DiscountAmount, 0)) AS RemainingAmount,
//                        CASE
//                            WHEN ri.DueDate < GETDATE() AND ps.WaveLate = 0
//                            THEN ROUND(ri.TotalRent * 0.05, 0)
//                            ELSE 0
//                        END AS LateFee,
//                        ps.WaveLate,
//                        ps.DiscountAmount,
//                        ps.DiscountPercent,
//                        0 AS PayAmount,
//                        NULL AS PaymentDate,
//                        NULL AS Method,
//                        '' AS Notes,
//                        SUM(ri.TotalRent - ISNULL(ps.Paid, 0) - ISNULL(ps.DiscountAmount, 0))
//                            OVER(PARTITION BY ri.TenantId ORDER BY ri.DueDate
//                                 ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING) AS PreviousBalance,
//                        SUM(CASE WHEN ri.DueDate < GETDATE() AND ps.WaveLate = 0
//                                 THEN ROUND(ri.TotalRent * 0.05, 0) ELSE 0 END)
//                            OVER(PARTITION BY ri.TenantId) AS TotalLateFeePerTenant,
//                        SUM(ps.Paid) OVER(PARTITION BY ri.TenantId) AS TotalPaidPerTenant
//                    FROM RentInvoices ri
//                    LEFT JOIN PaymentSums ps ON ri.Id = ps.InvoiceId
//                    LEFT JOIN Tenants t ON ri.TenantId = t.TenantId
//                    WHERE ri.StatusId IN (2, 3, 4, 8)
//                      AND ri.TenantId = @TenantId
//                )
//                SELECT *
//                FROM InvoiceWithTotals
//                ORDER BY InvoiceDate DESC;";

//            var parameters = new[] { new SqlParameter("@TenantId", tenantId) };
//            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
//        }

//        // =========================
//        // Payment writes
//        // =========================

//        public async Task<ApiResponse> CreatePaymentAdjustmentAsync(Payments payment, int userId)
//        {
//            string query = @"
//                INSERT INTO Payments
//                    (TenantId, PaymentAmount, PaymentDate, RentInvoiceId, PaymentMethod, Notes,
//                        DiscountAmount, DiscountPercent, IsLateFeeWaived, CreatedBy, CreatedAt)
//                VALUES
//                    (@TenantId, @PaymentAmount, GETDATE(), @RentInvoiceId, @PaymentMethod, @Notes,
//                        @DiscountAmount, @DiscountPercent, @IsLateFeeWaived, @CreatedBy, GETDATE());";

//            var parameters = new[]
//            {
//                new SqlParameter("@TenantId", payment.TenantId),
//                new SqlParameter("@PaymentAmount", payment.PaymentAmount),
//                new SqlParameter("@RentInvoiceId", payment.RentInvoiceId),
//                new SqlParameter("@PaymentMethod", string.IsNullOrWhiteSpace(payment.PaymentMethod) ? (object)DBNull.Value : payment.PaymentMethod),
//                new SqlParameter("@Notes", string.IsNullOrWhiteSpace(payment.Notes) ? (object)DBNull.Value : payment.Notes),
//                new SqlParameter("@DiscountAmount", payment.DiscountAmount),
//                new SqlParameter("@DiscountPercent", payment.DiscountPercent),
//                new SqlParameter("@IsLateFeeWaived", payment.IsLateFeeWaived),
//                new SqlParameter("@CreatedBy", userId),
//            };

//            return await _dbHelper.ExecuteQueryAsync(query, parameters);
//        }

//        public async Task<ApiResponse> CreateRentAsync(Payments payment, int userId, SqlConnection conn, SqlTransaction transaction)
//        {
//            string query = @"
//                INSERT INTO Payments
//                    (TenantId, PaymentAmount, PaymentDate, RentInvoiceId, PaymentMethod, Notes,
//                        DiscountAmount, DiscountPercent, IsLateFeeWaived, CreatedBy, CreatedAt)
//                VALUES
//                    (@TenantId, @PaymentAmount, @PaymentDate, @RentInvoiceId, @PaymentMethod, @Notes,
//                        @DiscountAmount, @DiscountPercent, @IsLateFeeWaived, @CreatedBy, GETDATE());";

//            var parameters = new[]
//            {
//                new SqlParameter("@TenantId", payment.TenantId),
//                new SqlParameter("@PaymentAmount", payment.PaymentAmount),
//                new SqlParameter("@PaymentDate", payment.PaymentDate),
//                new SqlParameter("@RentInvoiceId", payment.RentInvoiceId),
//                // FIX: this was previously bound to payment.RentInvoiceId instead of
//                // payment.PaymentMethod — every payment's method was being stored as
//                // the invoice ID number instead of "Cash"/"Bank Transfer"/etc.
//                new SqlParameter("@PaymentMethod", string.IsNullOrWhiteSpace(payment.PaymentMethod) ? (object)DBNull.Value : payment.PaymentMethod),
//                new SqlParameter("@Notes", string.IsNullOrWhiteSpace(payment.Notes) ? (object)DBNull.Value : payment.Notes),
//                new SqlParameter("@DiscountAmount", payment.DiscountAmount),
//                new SqlParameter("@DiscountPercent", payment.DiscountPercent),
//                new SqlParameter("@IsLateFeeWaived", payment.IsLateFeeWaived),
//                new SqlParameter("@CreatedBy", userId),
//            };

//            return await _dbHelper.ExecuteQueryAsync(query, parameters, conn, transaction);
//        }

//        private const string UpdateInvoiceAfterRentSql = @"
//            UPDATE RentInvoices
//            SET
//                PendingAmount = Calc.NewPending,
//                OverPaidAmount = Calc.Overpaid,
//                StatusId = CASE
//                               WHEN Calc.NewPending = 0 AND Calc.Overpaid > 0 THEN 9  -- Overpaid
//                               WHEN Calc.NewPending = 0 AND Calc.Overpaid = 0 THEN 1  -- Paid exactly
//                               WHEN Calc.NewPending > 0 AND Calc.NewPending < TotalRent THEN 8  -- Partial
//                               WHEN Calc.NewPending = PendingAmount THEN 2  -- Unpaid (no payment made)
//                               ELSE 8
//                           END
//            FROM RentInvoices
//            CROSS APPLY (
//                SELECT
//                    CASE WHEN PendingAmount - @PaymentAmount < 0 THEN 0 ELSE PendingAmount - @PaymentAmount END AS NewPending,
//                    CASE WHEN PendingAmount - @PaymentAmount < 0 THEN @PaymentAmount - PendingAmount ELSE 0 END AS Overpaid
//            ) AS Calc
//            WHERE Id = @InvoiceId;";

//        public async Task<ApiResponse> UpdateInvoiceAfterRentAsync(int invoiceId, decimal paymentAmount, SqlConnection conn, SqlTransaction transaction)
//        {
//            var parameters = new[]
//            {
//                new SqlParameter("@InvoiceId", invoiceId),
//                new SqlParameter("@PaymentAmount", paymentAmount)
//            };

//            return await _dbHelper.ExecuteQueryAsync(UpdateInvoiceAfterRentSql, parameters, conn, transaction);
//        }

//        // Non-transactional variant, used for single one-off writes (e.g. a manual
//        // adjustment) that don't need to share a transaction with another insert.
//        public async Task<ApiResponse> UpdateInvoiceAfterRentAsync(int invoiceId, decimal paymentAmount)
//        {
//            var parameters = new[]
//            {
//                new SqlParameter("@InvoiceId", invoiceId),
//                new SqlParameter("@PaymentAmount", paymentAmount)
//            };

//            return await _dbHelper.ExecuteQueryAsync(UpdateInvoiceAfterRentSql, parameters);
//        }

//        // Deletes the most recent payment on this invoice, then recomputes the
//        // invoice's PendingAmount/OverPaidAmount/StatusId from whatever payments
//        // remain — this keeps the invoice consistent with the payment ledger
//        // rather than trying to reverse the math incrementally.
//        public async Task<ApiResponse> DeleteLastPaymentForInvoice(int invoiceId)
//        {
//            string query = @"
//                DELETE FROM Payments WHERE Id = (
//                    SELECT TOP 1 Id FROM Payments WHERE RentInvoiceId = @InvoiceId ORDER BY CreatedAt DESC, Id DESC
//                );

//                UPDATE ri
//                SET
//                    PendingAmount = CASE WHEN (ri.TotalRent - ISNULL(pt.Paid,0) - ISNULL(pt.Disc,0)) < 0 THEN 0
//                                         ELSE (ri.TotalRent - ISNULL(pt.Paid,0) - ISNULL(pt.Disc,0)) END,
//                    OverPaidAmount = CASE WHEN (ri.TotalRent - ISNULL(pt.Paid,0) - ISNULL(pt.Disc,0)) < 0
//                                         THEN ABS(ri.TotalRent - ISNULL(pt.Paid,0) - ISNULL(pt.Disc,0)) ELSE 0 END,
//                    StatusId = CASE
//                        WHEN (ri.TotalRent - ISNULL(pt.Paid,0) - ISNULL(pt.Disc,0)) < 0 THEN 9  -- Overpaid
//                        WHEN (ri.TotalRent - ISNULL(pt.Paid,0) - ISNULL(pt.Disc,0)) = 0 THEN 1  -- Paid
//                        WHEN (ri.TotalRent - ISNULL(pt.Paid,0) - ISNULL(pt.Disc,0)) < ri.TotalRent THEN 8  -- Partial
//                        ELSE 2  -- Unpaid
//                    END
//                FROM RentInvoices ri
//                OUTER APPLY (
//                    SELECT SUM(PaymentAmount) AS Paid, SUM(DiscountAmount) AS Disc
//                    FROM Payments WHERE RentInvoiceId = ri.Id
//                ) pt
//                WHERE ri.Id = @InvoiceId;";

//            var parameters = new[] { new SqlParameter("@InvoiceId", invoiceId) };
//            return await _dbHelper.ExecuteQueryAsync(query, parameters);
//        }

//        // =========================
//        // Rent Collection list
//        // =========================

//        public async Task<DataTable> GetRentCollectionAsync()
//        {
//            // FIX: was joining Buildings ON u.BuildingId (doesn't exist on Units) —
//            // now goes through Floors correctly.
//            string query = @"
//                SELECT
//                    ri.Id AS InvoiceId,
//                    t.TenantId,
//                    t.Name AS TenantName,
//                    b.BuildingName,
//                    f.FloorNumber,
//                    u.UnitNumber,
//                    ri.InvoiceDate,
//                    ISNULL(ri.TotalRent, 0) AS MonthlyRent,
//                    ri.DueDate,

//                    ISNULL(ri.TotalRent - SUM(ISNULL(p.PaymentAmount, 0)) - SUM(ISNULL(p.DiscountAmount, 0)), 0)
//                        AS RemainingAmount,

//                    CASE
//                        WHEN (ri.TotalRent - SUM(ISNULL(p.PaymentAmount, 0)) - SUM(ISNULL(p.DiscountAmount, 0))) > 0
//                             AND ri.DueDate < CAST(GETUTCDATE() AS DATE)
//                             AND NOT EXISTS (
//                                 SELECT 1 FROM Payments p2
//                                 WHERE p2.RentInvoiceId = ri.Id
//                                   AND ISNULL(p2.IsLateFeeWaived, 0) = 1
//                             )
//                        THEN dbo.CalculateLateFee(
//                                 (ri.TotalRent - SUM(ISNULL(p.PaymentAmount, 0)) - SUM(ISNULL(p.DiscountAmount, 0))),
//                                 ri.TotalRent,
//                                 ri.DueDate,
//                                 GETUTCDATE()
//                             )
//                        ELSE 0
//                    END AS LateFee,

//                    s.StatusName,
//                    SUM(ISNULL(p.PaymentAmount, 0)) AS PaidAmount,
//                    SUM(ISNULL(p.DiscountAmount, 0)) AS AppliedDiscount,
//                    MAX(p.PaymentDate) AS LastPaymentDate
//                FROM RentInvoices ri
//                INNER JOIN Tenants t ON ri.TenantId = t.TenantId
//                INNER JOIN Units u ON t.UnitId = u.UnitId
//                INNER JOIN Floors f ON u.FloorId = f.FloorId
//                INNER JOIN Buildings b ON f.BuildingId = b.BuildingId
//                INNER JOIN StatusList s ON ri.StatusId = s.StatusId
//                LEFT JOIN Payments p ON ri.Id = p.RentInvoiceId
//                WHERE ri.StatusId IN (2, 8, 9)
//                GROUP BY
//                    ri.Id, t.TenantId, t.Name, b.BuildingName, f.FloorNumber, u.UnitNumber,
//                    ri.InvoiceDate, ri.TotalRent, ri.DueDate, s.StatusName
//                ORDER BY ri.InvoiceDate DESC, t.Name;";

//            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query);
//        }

//        public async Task<DataTable> GetPaymentHistoryAsync(int invoiceId)
//        {
//            // FIX: was selecting "StatusId" from Payments — that column doesn't
//            // exist on the Payments table (only on RentInvoices).
//            string query = @"
//                SELECT PaymentAmount, PaymentDate, PaymentMethod
//                FROM Payments
//                WHERE RentInvoiceId = @InvoiceId
//                ORDER BY PaymentDate ASC;";

//            var parameters = new[] { new SqlParameter("@InvoiceId", invoiceId) };
//            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
//        }

//        public async Task<ApiResponse> BulkUpdateDueDateAsync(List<int> invoiceIds, DateTime newDueDate)
//        {
//            var inParams = invoiceIds.Select((id, index) => $"@Id{index}").ToArray();
//            string query = $@"
//                UPDATE RentInvoices
//                SET DueDate = @NewDueDate
//                WHERE Id IN ({string.Join(", ", inParams)});";

//            var parameters = invoiceIds
//                .Select((id, index) => new SqlParameter($"@Id{index}", id))
//                .ToList();
//            parameters.Add(new SqlParameter("@NewDueDate", newDueDate));

//            return await _dbHelper.ExecuteQueryAsync(query, parameters.ToArray());
//        }

//        // =========================
//        // Bulk invoice generation (manual button — no scheduled service)
//        // =========================

//        public async Task<DataTable> GetActiveTenantsWithoutInvoiceForMonth(int month, int year)
//        {
//            string query = @"
//                SELECT t.TenantId, t.MonthlyRent
//                FROM Tenants t
//                WHERE t.IsActive = 1
//                  AND t.MoveOutDate IS NULL
//                  AND NOT EXISTS (
//                        SELECT 1 FROM RentInvoices ri
//                        WHERE ri.TenantId = t.TenantId
//                          AND MONTH(ri.InvoiceDate) = @Month
//                          AND YEAR(ri.InvoiceDate) = @Year
//                  );";

//            var parameters = new[]
//            {
//                new SqlParameter("@Month", month),
//                new SqlParameter("@Year", year),
//            };

//            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
//        }

//        public async Task<ApiResponse> CreateInvoice(int tenantId, decimal totalRent, DateTime invoiceDate, DateTime dueDate)
//        {
//            // StatusId 2 = Unpaid. NOT 3 (Pending): GetRentCollectionAsync only
//            // shows StatusId IN (2,8,9), so a Pending invoice would never surface
//            // in the collections list until something else changed its status —
//            // and since there's no scheduled job, it would sit invisible forever.
//            string query = @"
//                INSERT INTO RentInvoices
//                    (TenantId, TotalRent, PendingAmount, OverPaidAmount, InvoiceDate, DueDate, StatusId, CreatedAt)
//                VALUES
//                    (@TenantId, @TotalRent, @TotalRent, 0, @InvoiceDate, @DueDate, 2, GETDATE());";

//            var parameters = new[]
//            {
//                new SqlParameter("@TenantId", tenantId),
//                new SqlParameter("@TotalRent", totalRent),
//                new SqlParameter("@InvoiceDate", invoiceDate),
//                new SqlParameter("@DueDate", dueDate),
//            };

//            return await _dbHelper.ExecuteQueryAsync(query, parameters);
//        }
//    }
//}



using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.Data;
using TRL_API.Models;

namespace TRL_API.DAL
{
    public class RentRepository
    {
        private readonly DbHelper _dbHelper;

        public RentRepository(DbHelper dbHelper)
        {
            _dbHelper = dbHelper;
        }

        // Delegates to DbHelper — add GetOpenConnectionAsync() there (see chat).
        // Submitting multiple payments needs one open SqlConnection + SqlTransaction
        // shared across all the inserts/updates, so a failure partway through rolls
        // everything back instead of leaving half the payments recorded.
        public async Task<SqlConnection> GetOpenConnectionAsync() => await _dbHelper.GetOpenConnectionAsync();

        // =========================
        // Data Retrieval Methods
        // =========================

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

        public async Task<DataTable> GetStatusListAsync()
        {
            string query = @"
                SELECT StatusId, StatusName
                FROM StatusList
                WHERE IsActive = 1;";

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
                INNER JOIN Units u ON t.UnitId = u.UnitId
                INNER JOIN Floors f ON u.FloorId = f.FloorId
                INNER JOIN Buildings b ON f.BuildingId = b.BuildingId
                INNER JOIN StatusList s ON ri.StatusId = s.StatusId
                WHERE ri.TenantId = @TenantId
                ORDER BY ri.InvoiceDate DESC;";

            var parameters = new[] { new SqlParameter("@TenantId", tenantId) };
            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
        }

        public async Task<DataTable> GetInvoiceByIdAsync(int invoiceId)
        {
            // FIX: was joining Buildings ON u.BuildingId (Units has no BuildingId
            // column) — now correctly goes through Floors, same as GetInvoicesByTenantAsync.
            string query = @"
                SELECT
                    ri.Id AS InvoiceId,
                    p.Id AS PaymentId,
                    ri.TenantId,
                    t.Name AS TenantName,
                    b.BuildingName,
                    f.FloorNumber,
                    u.UnitNumber,
                    ri.InvoiceDate,
                    ri.TotalRent AS MonthlyRent,
                    ri.DueDate,

                    (ri.TotalRent - ISNULL(SUM(p.PaymentAmount) OVER (PARTITION BY ri.Id), 0)
                                   - ISNULL(SUM(p.DiscountAmount) OVER (PARTITION BY ri.Id), 0)) AS RemainingAmount,

                    ri.StatusId,
                    s.StatusName,
                    ISNULL(p.PaymentAmount, 0) AS PaidAmount,
                    p.PaymentDate,
                    p.PaymentMethod,
                    p.Notes,
                    p.CreatedAt,
                    p.DiscountAmount,
                    p.DiscountPercent,
                    p.IsLateFeeWaived
                FROM RentInvoices ri
                INNER JOIN Tenants t ON ri.TenantId = t.TenantId
                INNER JOIN Units u ON t.UnitId = u.UnitId
                INNER JOIN Floors f ON u.FloorId = f.FloorId
                INNER JOIN Buildings b ON f.BuildingId = b.BuildingId
                INNER JOIN StatusList s ON ri.StatusId = s.StatusId
                LEFT JOIN Payments p ON ri.Id = p.RentInvoiceId
                WHERE ri.Id = @InvoiceId
                ORDER BY p.PaymentDate DESC;";

            var parameters = new[] { new SqlParameter("@InvoiceId", invoiceId) };
            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
        }

        //public async Task<DataTable> GetPaymentHistoryByIdAsync(int invoiceId)
        //{
        //    string query = @"
        //        SELECT
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
        //        FROM Payments p
        //        INNER JOIN RentInvoices ri ON p.RentInvoiceId = ri.Id
        //        WHERE ri.Id = @InvoiceId
        //        ORDER BY p.PaymentDate DESC, p.Id DESC;";

        //    var parameters = new[] { new SqlParameter("@InvoiceId", invoiceId) };
        //    return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
        //}

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


        //public async Task<DataTable> GetUnpaidInvoiceByTenant(int tenantId)
        //{
        //    string query = @"
        //        WITH PaymentSums AS (
        //            SELECT
        //                ri.Id AS InvoiceId,
        //                ISNULL(SUM(p.PaymentAmount), 0) AS Paid,
        //                ISNULL(SUM(p.DiscountAmount), 0) AS DiscountAmount,
        //                ISNULL(MAX(p.DiscountPercent), 0) AS DiscountPercent,
        //                MAX(CASE WHEN p.IsLateFeeWaived = 1 THEN 1 ELSE 0 END) AS WaveLate
        //            FROM RentInvoices ri
        //            LEFT JOIN Payments p ON ri.Id = p.RentInvoiceId
        //            GROUP BY ri.Id
        //        ),
        //        InvoiceWithTotals AS (
        //            SELECT
        //                ri.Id AS InvoiceId,
        //                ri.TenantId,
        //                t.Name AS TenantName,
        //                ri.InvoiceDate,
        //                ri.DueDate,
        //                ri.TotalRent AS MonthlyRent,
        //                ps.Paid AS PaidAmount,
        //                (ri.TotalRent - ISNULL(ps.Paid, 0) - ISNULL(ps.DiscountAmount, 0)) AS RemainingAmount,
        //                CASE
        //                    WHEN ri.DueDate < GETDATE() AND ps.WaveLate = 0
        //                    THEN ROUND(ri.TotalRent * 0.05, 0)
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
        //            FROM RentInvoices ri
        //            LEFT JOIN PaymentSums ps ON ri.Id = ps.InvoiceId
        //            LEFT JOIN Tenants t ON ri.TenantId = t.TenantId
        //            WHERE ri.StatusId IN (2, 3, 4, 8)
        //              AND ri.TenantId = @TenantId
        //        )
        //        SELECT *
        //        FROM InvoiceWithTotals
        //        ORDER BY InvoiceDate DESC;";

        //    var parameters = new[] { new SqlParameter("@TenantId", tenantId) };
        //    return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
        //}

        public async Task<DataTable> GetUnpaidInvoiceByTenant(int tenantId)
        {
            string query = @"
                WITH PaymentSums AS (
                    SELECT ri.Id AS InvoiceId,
                           ISNULL(SUM(p.PaymentAmount), 0) AS Paid,
                           ISNULL(SUM(p.DiscountAmount), 0) AS DiscountAmount,
                           ISNULL(MAX(p.DiscountPercent), 0) AS DiscountPercent,
                           MAX(CASE WHEN p.IsLateFeeWaived = 1 THEN 1 ELSE 0 END) AS WaveLate
                    FROM RentInvoices ri
                    LEFT JOIN Payments p ON ri.Id = p.RentInvoiceId
                    GROUP BY ri.Id
                ),
                Base AS (
                    SELECT ri.Id AS InvoiceId, ri.TenantId, t.Name AS TenantName, ri.InvoiceDate, ri.DueDate,
                           ri.TotalRent AS MonthlyRent, ps.Paid AS PaidAmount, ps.DiscountAmount, ps.DiscountPercent,
                           ps.WaveLate, ri.LateFeeCharged,
                           (ri.TotalRent + ri.LateFeeCharged - ps.Paid - ps.DiscountAmount) AS RemainingAmount,
                           CASE WHEN ri.LateFeeCharged = 0 AND ps.WaveLate = 0
                                     AND ri.DueDate < CAST(GETUTCDATE() AS DATE)
                                     AND (ri.TotalRent - ps.Paid - ps.DiscountAmount) > 0
                                THEN CAST(dbo.CalculateLateFee(ri.TotalRent - ps.Paid - ps.DiscountAmount,
                                          ri.TotalRent, ri.DueDate, GETUTCDATE()) AS DECIMAL(18,2))
                                ELSE 0 END AS LateFee
                    FROM RentInvoices ri
                    INNER JOIN PaymentSums ps ON ri.Id = ps.InvoiceId
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

        public async Task<ApiResponse> CreatePaymentAdjustmentAsync(Payments payment, int userId)
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

            return await _dbHelper.ExecuteQueryAsync(query, parameters);
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

        //private const string UpdateInvoiceAfterRentSql = @"
        //    UPDATE RentInvoices
        //    SET
        //        PendingAmount = Calc.NewPending,
        //        OverPaidAmount = Calc.Overpaid,
        //        StatusId = CASE
        //                       WHEN Calc.NewPending = 0 AND Calc.Overpaid > 0 THEN 9  -- Overpaid
        //                       WHEN Calc.NewPending = 0 AND Calc.Overpaid = 0 THEN 1  -- Paid exactly
        //                       WHEN Calc.NewPending > 0 AND Calc.NewPending < TotalRent THEN 8  -- Partial
        //                       WHEN Calc.NewPending = PendingAmount THEN 2  -- Unpaid (no payment made)
        //                       ELSE 8
        //                   END
        //    FROM RentInvoices
        //    CROSS APPLY (
        //        SELECT
        //            CASE WHEN PendingAmount - @PaymentAmount < 0 THEN 0 ELSE PendingAmount - @PaymentAmount END AS NewPending,
        //            CASE WHEN PendingAmount - @PaymentAmount < 0 THEN @PaymentAmount - PendingAmount ELSE 0 END AS Overpaid
        //    ) AS Calc
        //    WHERE Id = @InvoiceId;";

        private const string RecalcInvoiceSql = @"
            UPDATE ri SET
                PendingAmount  = CASE WHEN x.Bal < 0 THEN 0 ELSE x.Bal END,
                OverPaidAmount = CASE WHEN x.Bal < 0 THEN -x.Bal ELSE 0 END,
                StatusId = CASE WHEN x.Bal < 0 THEN 9
                                WHEN x.Bal = 0 THEN 1
                                WHEN p.Paid + p.Disc > 0 THEN 8
                                ELSE 2 END
            FROM RentInvoices ri
            OUTER APPLY (SELECT ISNULL(SUM(PaymentAmount),0) AS Paid, ISNULL(SUM(DiscountAmount),0) AS Disc
                         FROM Payments WHERE RentInvoiceId = ri.Id) p
            CROSS APPLY (SELECT ri.TotalRent + ri.LateFeeCharged - p.Paid - p.Disc AS Bal) x
            WHERE ri.Id = @InvoiceId AND ri.StatusId <> 6;";

        private const string UpdateInvoiceAfterRentSql = RecalcInvoiceSql;

        public async Task<ApiResponse> UpdateInvoiceAfterRentAsync(int invoiceId, decimal paymentAmount, SqlConnection conn, SqlTransaction transaction)
        {
            var parameters = new[]
            {
                new SqlParameter("@InvoiceId", invoiceId),
                new SqlParameter("@PaymentAmount", paymentAmount)
            };

            return await _dbHelper.ExecuteQueryAsync(UpdateInvoiceAfterRentSql, parameters, conn, transaction);
        }

        // Non-transactional variant, used for single one-off writes (e.g. a manual
        // adjustment) that don't need to share a transaction with another insert.
        public async Task<ApiResponse> UpdateInvoiceAfterRentAsync(int invoiceId, decimal paymentAmount)
        {
            var parameters = new[]
            {
                new SqlParameter("@InvoiceId", invoiceId),
                new SqlParameter("@PaymentAmount", paymentAmount)
            };

            return await _dbHelper.ExecuteQueryAsync(UpdateInvoiceAfterRentSql, parameters);
        }

        // Deletes the most recent payment on this invoice, then recomputes the
        // invoice's PendingAmount/OverPaidAmount/StatusId from whatever payments
        // remain — this keeps the invoice consistent with the payment ledger
        // rather than trying to reverse the math incrementally.
        //public async Task<ApiResponse> DeleteLastPaymentForInvoice(int invoiceId)
        //{
        //    string query = @"
        //        DELETE FROM Payments WHERE Id = (
        //            SELECT TOP 1 Id FROM Payments WHERE RentInvoiceId = @InvoiceId ORDER BY CreatedAt DESC, Id DESC
        //        );

        //        UPDATE ri
        //        SET
        //            PendingAmount = CASE WHEN (ri.TotalRent - ISNULL(pt.Paid,0) - ISNULL(pt.Disc,0)) < 0 THEN 0
        //                                 ELSE (ri.TotalRent - ISNULL(pt.Paid,0) - ISNULL(pt.Disc,0)) END,
        //            OverPaidAmount = CASE WHEN (ri.TotalRent - ISNULL(pt.Paid,0) - ISNULL(pt.Disc,0)) < 0
        //                                 THEN ABS(ri.TotalRent - ISNULL(pt.Paid,0) - ISNULL(pt.Disc,0)) ELSE 0 END,
        //            StatusId = CASE
        //                WHEN (ri.TotalRent - ISNULL(pt.Paid,0) - ISNULL(pt.Disc,0)) < 0 THEN 9  -- Overpaid
        //                WHEN (ri.TotalRent - ISNULL(pt.Paid,0) - ISNULL(pt.Disc,0)) = 0 THEN 1  -- Paid
        //                WHEN (ri.TotalRent - ISNULL(pt.Paid,0) - ISNULL(pt.Disc,0)) < ri.TotalRent THEN 8  -- Partial
        //                ELSE 2  -- Unpaid
        //            END
        //        FROM RentInvoices ri
        //        OUTER APPLY (
        //            SELECT SUM(PaymentAmount) AS Paid, SUM(DiscountAmount) AS Disc
        //            FROM Payments WHERE RentInvoiceId = ri.Id
        //        ) pt
        //        WHERE ri.Id = @InvoiceId;";

        //    var parameters = new[] { new SqlParameter("@InvoiceId", invoiceId) };
        //    return await _dbHelper.ExecuteQueryAsync(query, parameters);
        //}

        public async Task<ApiResponse> DeleteLastPaymentForInvoice(int invoiceId)
        {
            string query = @"
                DELETE FROM Payments WHERE Id = (
                    SELECT TOP 1 Id FROM Payments WHERE RentInvoiceId = @InvoiceId 
                    ORDER BY CreatedAt DESC, Id DESC);" + RecalcInvoiceSql;
            return await _dbHelper.ExecuteQueryAsync(query, new[] { new SqlParameter("@InvoiceId", invoiceId) });
        }

        // Server-side payment rules (inside the same transaction as the insert)
        public async Task<string?> ValidatePaymentAsync(Payments p, SqlConnection conn, SqlTransaction tx)
        {
            const string q = @"
                SELECT ri.TenantId, ri.StatusId,
                       ri.TotalRent + ri.LateFeeCharged - pt.Paid - pt.Disc AS Balance,
                       ri.TotalRent - pt.Paid - pt.Disc AS RentBalance,
                       CASE WHEN ri.LateFeeCharged = 0 AND ri.DueDate < CAST(GETUTCDATE() AS DATE)
                                 AND NOT EXISTS (SELECT 1 FROM Payments w
                                                 WHERE w.RentInvoiceId = ri.Id AND ISNULL(w.IsLateFeeWaived,0) = 1)
                            THEN dbo.CalculateLateFee(ri.TotalRent - pt.Paid - pt.Disc, ri.TotalRent, ri.DueDate, GETUTCDATE())
                            ELSE 0 END AS OpenFee
                FROM RentInvoices ri
                CROSS APPLY (SELECT ISNULL(SUM(PaymentAmount),0) AS Paid, ISNULL(SUM(DiscountAmount),0) AS Disc
                             FROM Payments WHERE RentInvoiceId = ri.Id) pt
                WHERE ri.Id = @Id;";

            using var cmd = new SqlCommand(q, conn, tx);
            cmd.Parameters.AddWithValue("@Id", p.RentInvoiceId);
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
                SELECT @Fee = ri.LateFeeCharged
                FROM RentInvoices ri
                OUTER APPLY (SELECT SUM(PaymentAmount) Paid, SUM(DiscountAmount) Disc
                             FROM Payments WHERE RentInvoiceId = ri.Id) pt
                WHERE ri.Id = @InvoiceId AND ri.LateFeeCharged > 0 AND ri.StatusId <> 6
                  AND ISNULL(pt.Paid,0) + ISNULL(pt.Disc,0) <= ri.TotalRent;

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

        public async Task<decimal> GetUnitRentAsync(int unitId)
        {
            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(
                "SELECT UnitRent FROM vw_UnitOccupancy WHERE UnitId = @Id",
                new[] { new SqlParameter("@Id", unitId) });
            return dt.Rows.Count > 0 && dt.Rows[0]["UnitRent"] != DBNull.Value ? Convert.ToDecimal(dt.Rows[0]["UnitRent"]) : 0m;
        }


        // =========================
        // Rent Collection list
        // =========================

        public async Task<DataTable> GetRentCollectionAsync()
        {
            // FIX: was joining Buildings ON u.BuildingId (doesn't exist on Units) —
            // now goes through Floors correctly.
            string query = @"
                SELECT
                    ri.Id AS InvoiceId,
                    t.TenantId,
                    t.Name AS TenantName,
                    b.BuildingName,
                    f.FloorNumber,
                    u.UnitNumber,
                    ri.InvoiceDate,
                    ri.LateFeeCharged,
                    ISNULL(ri.TotalRent, 0) AS MonthlyRent,
                    ri.DueDate,

                    ISNULL(
                        ri.TotalRent + ri.LateFeeCharged
                        - SUM(ISNULL(p.PaymentAmount, 0))
                        - SUM(ISNULL(p.DiscountAmount, 0)),
                        0
                    ) AS RemainingAmount,

                    CASE
                        WHEN (ri.TotalRent - SUM(ISNULL(p.PaymentAmount, 0)) - SUM(ISNULL(p.DiscountAmount, 0))) > 0
                             AND ri.DueDate < CAST(GETUTCDATE() AS DATE)
                             AND ri.LateFeeCharged = 0
                             AND NOT EXISTS (
                                 SELECT 1 FROM Payments p2
                                 WHERE p2.RentInvoiceId = ri.Id
                                   AND ISNULL(p2.IsLateFeeWaived, 0) = 1
                             )
                        THEN dbo.CalculateLateFee(
                                 (ri.TotalRent - SUM(ISNULL(p.PaymentAmount, 0)) - SUM(ISNULL(p.DiscountAmount, 0))),
                                 ri.TotalRent,
                                 ri.DueDate,
                                 GETUTCDATE()
                             )
                        ELSE 0
                    END AS LateFee,

                    s.StatusName,
                    SUM(ISNULL(p.PaymentAmount, 0)) AS PaidAmount,
                    SUM(ISNULL(p.DiscountAmount, 0)) AS AppliedDiscount,
                    MAX(p.PaymentDate) AS LastPaymentDate,
                    ri.Description,
                    ri.ChargeType,

                    (
                        SELECT ISNULL(SUM(ri2.TotalRent - ISNULL(p3.Paid,0) - ISNULL(p3.Disc,0)), 0)
                        FROM RentInvoices ri2
                        OUTER APPLY (
                            SELECT SUM(PaymentAmount) AS Paid, SUM(DiscountAmount) AS Disc
                            FROM Payments WHERE RentInvoiceId = ri2.Id
                        ) p3
                        WHERE ri2.TenantId = t.TenantId AND ri2.Id <> ri.Id AND ri2.StatusId IN (2,8,9)
                    ) AS PreviousBalance
                FROM RentInvoices ri
                INNER JOIN Tenants t ON ri.TenantId = t.TenantId
                INNER JOIN Units u ON t.UnitId = u.UnitId
                INNER JOIN Floors f ON u.FloorId = f.FloorId
                INNER JOIN Buildings b ON f.BuildingId = b.BuildingId
                INNER JOIN StatusList s ON ri.StatusId = s.StatusId
                LEFT JOIN Payments p ON ri.Id = p.RentInvoiceId
                WHERE ri.StatusId IN (2, 8, 9)
                GROUP BY
                    ri.Id, t.TenantId, t.Name, b.BuildingName, f.FloorNumber, u.UnitNumber, ri.LateFeeCharged,
                    ri.InvoiceDate, ri.TotalRent, ri.DueDate, s.StatusName, ri.Description, ri.ChargeType
                ORDER BY ri.InvoiceDate DESC, t.Name;";

            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query);
        }

        public async Task<DataTable> GetTenantsWithRent()
        {
            string query = @"SELECT TenantId, Name, MonthlyRent FROM Tenants WHERE IsActive = 1 ORDER BY Name;";
            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query);
        }

        public async Task<ApiResponse> UpdateTenantMonthlyRent(int tenantId, decimal newRent)
        {
            string query = @"UPDATE Tenants SET MonthlyRent = @MonthlyRent WHERE TenantId = @TenantId;";
            var parameters = new[]
            {
        new SqlParameter("@TenantId", tenantId),
        new SqlParameter("@MonthlyRent", newRent),
    };
            return await _dbHelper.ExecuteQueryAsync(query, parameters);
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

        //public async Task<DataTable> GetActiveTenantsWithoutInvoiceForMonth(int month, int year)
        //{
        //    string query = @"
        //        SELECT t.TenantId, t.MonthlyRent
        //        FROM Tenants t
        //        WHERE t.IsActive = 1
        //          AND t.MoveOutDate IS NULL
        //          AND NOT EXISTS (
        //                SELECT 1 FROM RentInvoices ri
        //                WHERE ri.TenantId = t.TenantId
        //                  AND MONTH(ri.InvoiceDate) = @Month
        //                  AND YEAR(ri.InvoiceDate) = @Year
        //                  AND ri.StatusId <> 6
        //                  AND ri.ChargeType IS NULL -- only counts regular rent invoices, not extra charges
        //          );";

        //    var parameters = new[]
        //    {
        //        new SqlParameter("@Month", month),
        //        new SqlParameter("@Year", year),
        //    };

        //    return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
        //}

        public async Task<DataTable> GetActiveTenantsWithoutInvoiceForMonth(int month, int year)
        {
            string query = @"
                SELECT tl.TenantId, tl.UnitId, tl.RentAmount AS MonthlyRent
                FROM TenantLeases tl
                JOIN Tenants t ON t.TenantId = tl.TenantId
                WHERE tl.IsActive = 1 AND t.IsActive = 1
                  AND NOT EXISTS (
                        SELECT 1 FROM RentInvoices ri
                        WHERE ri.TenantId = tl.TenantId
                          AND MONTH(ri.InvoiceDate) = @Month AND YEAR(ri.InvoiceDate) = @Year
                          AND ri.StatusId <> 6 AND ri.ChargeType IS NULL
                  );";
            var parameters = new[] { new SqlParameter("@Month", month), new SqlParameter("@Year", year) };
            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
        }

        public async Task<decimal> GetTenantMonthlyRent(int tenantId)
        {
            string query = "SELECT MonthlyRent FROM Tenants WHERE TenantId = @TenantId";
            var parameters = new[] { new SqlParameter("@TenantId", tenantId) };
            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
            return dt.Rows.Count > 0 ? Convert.ToDecimal(dt.Rows[0]["MonthlyRent"]) : 0m;
        }

        // description/chargeType are null for a regular monthly rent invoice,
        // and populated for a one-time extra charge (Maintenance, Late Fine, etc.)
        public async Task<ApiResponse> CreateInvoice(
            int tenantId, decimal totalRent, DateTime invoiceDate, DateTime dueDate,
            string? description = null, string? chargeType = null)
        {
            // StatusId 2 = Unpaid. NOT 3 (Pending): GetRentCollectionAsync only
            // shows StatusId IN (2,8,9), so a Pending invoice would never surface
            // in the collections list until something else changed its status —
            // and since there's no scheduled job, it would sit invisible forever.
            string query = @"
                INSERT INTO RentInvoices
                    (TenantId, TotalRent, PendingAmount, OverPaidAmount, InvoiceDate, DueDate, StatusId, Description, ChargeType, CreatedAt)
                VALUES
                    (@TenantId, @TotalRent, @TotalRent, 0, @InvoiceDate, @DueDate, 2, @Description, @ChargeType, GETDATE());";

            var parameters = new[]
            {
                new SqlParameter("@TenantId", tenantId),
                new SqlParameter("@TotalRent", totalRent),
                new SqlParameter("@InvoiceDate", invoiceDate),
                new SqlParameter("@DueDate", dueDate),
                new SqlParameter("@Description", (object?)description ?? DBNull.Value),
                new SqlParameter("@ChargeType", (object?)chargeType ?? DBNull.Value),
            };

            return await _dbHelper.ExecuteQueryAsync(query, parameters);
        }


        //Newly Added
        public async Task<DataTable> ChargeLateFeeAsync(int invoiceId)
        {
            string query = @"
                DECLARE @Fee DECIMAL(18,2);
                SELECT @Fee = CAST(dbo.CalculateLateFee(
                        ri.TotalRent - ISNULL(pt.Paid,0) - ISNULL(pt.Disc,0),
                        ri.TotalRent, ri.DueDate, GETUTCDATE()) AS DECIMAL(18,2))
                FROM RentInvoices ri
                OUTER APPLY (SELECT SUM(PaymentAmount) Paid, SUM(DiscountAmount) Disc
                             FROM Payments WHERE RentInvoiceId = ri.Id) pt
                WHERE ri.Id = @InvoiceId
                  AND ri.LateFeeCharged = 0
                  AND ri.DueDate < CAST(GETUTCDATE() AS DATE)
                  AND (ri.TotalRent - ISNULL(pt.Paid,0) - ISNULL(pt.Disc,0)) > 0
                  AND NOT EXISTS (SELECT 1 FROM Payments w
                                  WHERE w.RentInvoiceId = ri.Id AND ISNULL(w.IsLateFeeWaived,0) = 1);

                IF ISNULL(@Fee,0) <= 0
                BEGIN
                    SELECT CAST(0 AS DECIMAL(18,2)) AS Fee;
                    RETURN;
                END

                UPDATE RentInvoices
                SET LateFeeCharged = @Fee, LateFeeChargedAt = GETDATE(), PendingAmount = PendingAmount + @Fee
                WHERE Id = @InvoiceId AND LateFeeCharged = 0;

                SELECT CASE WHEN @@ROWCOUNT = 1 THEN @Fee ELSE CAST(0 AS DECIMAL(18,2)) END AS Fee;";

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
                LEFT JOIN Units u ON u.UnitId = t.UnitId
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