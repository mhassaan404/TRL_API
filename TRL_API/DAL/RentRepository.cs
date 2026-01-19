using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.SqlClient;
using Microsoft.DotNet.Scaffolding.Shared.CodeModifier.CodeChange;
using System.Collections.Generic;
using System.Data;
using TRL_API.Data;
using TRL_API.Models;
using static Azure.Core.HttpHeader;

namespace TRL_API.DAL
{
    public class RentRepository
    {
        private readonly DbHelper _dbHelper;

        public RentRepository(DbHelper dbHelper)
        {
            _dbHelper = dbHelper;
        }

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
            ORDER BY t.TenantId;
            ";

            return await _dbHelper.ExecuteQueryAsync(query);
        }

        public async Task<DataTable> GetStatusListAsync()
        {
            string query = @"
                SELECT StatusId, StatusName
                FROM StatusList
                WHERE IsActive = 1;";

            return await _dbHelper.ExecuteQueryAsync(query);
        }

        public async Task<DataTable> GetInvoicesByTenantAsync(int tenantId)
        {
            string query = @"
                            SELECT 
                ri.Id AS InvoiceId,
                ri.InvoiceDate,
                ri.PendingAmount,
                ri.Status,
                u.UnitNumber,
                f.FloorNumber,
                b.BuildingName
            FROM RentInvoices ri
            INNER JOIN Tenants t ON ri.TenantId = t.TenantId
            INNER JOIN Units u ON t.UnitId = u.UnitId
            INNER JOIN Floors f ON u.FloorId = f.FloorId
            INNER JOIN Buildings b ON f.BuildingId = b.BuildingId
            WHERE ri.TenantId = @TenantId
            ORDER BY ri.InvoiceDate DESC;";

            var parameters = new[] { new SqlParameter("@TenantId", tenantId) };
            return await _dbHelper.ExecuteQueryAsync(query, parameters);
        }

        public async Task<DataTable> GetInvoiceByIdAsync(int invoiceId)
        {
            string query = @"SELECT
                ri.Id AS InvoiceId,
                p.Id                AS PaymentId,
                ri.TenantId,
                t.Name               AS TenantName,
                b.BuildingName,
                f.FloorNumber,
                u.UnitNumber,
            
                ri.InvoiceDate,
                ri.TotalRent AS MonthlyRent,
                ri.DueDate,
	            ri.PendingAmount AS RemainingAmount,
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
            INNER JOIN Tenants t      ON ri.TenantId = t.TenantId
            INNER JOIN Units u        ON t.UnitId = u.UnitId
            INNER JOIN Floors f       ON u.FloorId = f.FloorId
            INNER JOIN Buildings b    ON u.BuildingId = b.BuildingId
            INNER JOIN StatusList s   ON ri.StatusId = s.StatusId
            LEFT JOIN Payments p      ON ri.Id = p.RentInvoiceId
            WHERE ri.Id = @InvoiceId
            ORDER BY p.PaymentDate DESC;
            ";

            var parameters = new[] { new SqlParameter("@InvoiceId", invoiceId) };
            return await _dbHelper.ExecuteQueryAsync(query, parameters);
        }

        //public async Task<DataTable> GetPaymentHistoryByIdAsync(int invoiceId)
        //{
        //    string query = @"SELECT
        //        p.PaymentDate,
        //        ri.TotalRent AS MonthlyRent,
        //        ISNULL(p.PaymentAmount, 0) AS PaidAmount,
        //        -- Cumulative sum of payments + discounts to calculate remaining
        //        ri.TotalRent - ISNULL(SUM(p.PaymentAmount + p.DiscountAmount)
        //                              OVER (PARTITION BY ri.Id ORDER BY p.PaymentDate, p.Id
        //                                    ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW), 0) AS RemainingAmount,
        //        ISNULL(p.DiscountAmount, 0) AS DiscountAmount,
        //        ISNULL(p.DiscountPercent, 0) AS DiscountPercent,
        //        ISNULL(p.IsLateFeeWaived, 0) AS waveLateFee,
        //        ISNULL(p.PaymentMethod, '') AS PaymentMethod,
        //        ISNULL(p.Notes, '') AS Notes
        //    FROM Payments p
        //    INNER JOIN RentInvoices ri ON p.RentInvoiceId = ri.Id
        //    WHERE ri.Id = @invoiceId
        //    ORDER BY p.PaymentDate DESC, p.Id DESC;
        //    ";

        //    var parameters = new[] { new SqlParameter("@InvoiceId", invoiceId) };
        //    return await _dbHelper.ExecuteQueryAsync(query, parameters);
        //}

        public async Task<DataTable> GetPaymentHistoryByIdAsync(int invoiceId)
        {
            string query = @"SELECT
                p.PaymentDate,
                ri.TotalRent AS MonthlyRent,
                ISNULL(p.PaymentAmount, 0) AS PaidAmount,
                ri.TotalRent - ISNULL(SUM(p.PaymentAmount + p.DiscountAmount)
                                      OVER (PARTITION BY ri.Id ORDER BY p.PaymentDate, p.Id
                                            ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW), 0) AS RemainingAmount,
                
                -- NEW: Cumulative paid up to this payment
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
            WHERE ri.Id = @invoiceId
            ORDER BY p.PaymentDate DESC, p.Id DESC;
            ";

            var parameters = new[] { new SqlParameter("@InvoiceId", invoiceId) };
            return await _dbHelper.ExecuteQueryAsync(query, parameters);
        }

        //public async Task<DataTable> GetInvoiceDetailsAsync(int invoiceId)
        //{
        //    string query = @"
        //                    SELECT 
        //        ri.TotalRent, 
        //        ri.PendingAmount, 
        //        ri.DueDate, 
        //        u.UnitNumber, 
        //        b.BuildingName, 
        //        f.FloorNumber
        //    FROM RentInvoices ri
        //    INNER JOIN Tenants t ON ri.TenantId = t.TenantId
        //    INNER JOIN Units u ON t.UnitId = u.UnitId
        //    INNER JOIN Floors f ON u.FloorId = f.FloorId
        //    INNER JOIN Buildings b ON f.BuildingId = b.BuildingId
        //    WHERE ri.Id = @InvoiceId;
        //    ";

        //    var parameters = new[] { new SqlParameter("@InvoiceId", invoiceId) };
        //    return await _dbHelper.ExecuteQueryAsync(query, parameters);
        //}

        public async Task<DataTable> GetUnpaidInvoiceByTenant(int tenantId)
        {
            string query = @"
                        WITH PaymentSums AS (
                SELECT 
                    ri.Id AS InvoiceId,
                    ISNULL(SUM(p.PaymentAmount), 0) AS Paid,
                    ISNULL(SUM(p.DiscountAmount), 0) AS DiscountAmount,
                    ISNULL(MAX(p.DiscountPercent), 0) AS DiscountPercent,
                    MAX(CASE WHEN p.IsLateFeeWaived = 1 THEN 1 ELSE 0 END) AS WaveLate
                FROM RentInvoices ri
                LEFT JOIN Payments p ON ri.Id = p.RentInvoiceId
                GROUP BY ri.Id
            ),
            InvoiceWithTotals AS (
                SELECT
                    ri.Id AS InvoiceId,
                    ri.TenantId,
                    t.Name AS TenantName,
                    ri.InvoiceDate,
                    ri.DueDate,
                    ri.TotalRent AS MonthlyRent,
                    ps.Paid as PaidAmount,
                    ri.PendingAmount AS RemainingAmount,
                    CASE 
                        WHEN ri.DueDate < GETDATE() AND ps.WaveLate = 0
                        THEN ROUND(ri.TotalRent * 0.05, 0)
                        ELSE 0
                    END AS LateFee,
                    ps.WaveLate,
                    ps.DiscountAmount,
                    ps.DiscountPercent,
                    0 AS PayAmount,          -- placeholder for frontend input
                    NULL AS PaymentDate,     -- placeholder
                    NULL AS Method,          -- placeholder
                    '' AS Notes,             -- placeholder
                    SUM(ri.TotalRent - ps.Paid) OVER(PARTITION BY ri.TenantId ORDER BY ri.DueDate ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING) AS PreviousBalance,
                    SUM(CASE WHEN ri.DueDate < GETDATE() AND ps.WaveLate = 0 THEN ROUND(ri.TotalRent * 0.05, 0) ELSE 0 END) OVER(PARTITION BY ri.TenantId) AS   TotalLateFeePerTenant,
                    SUM(ps.Paid) OVER(PARTITION BY ri.TenantId) AS TotalPaidPerTenant
                FROM RentInvoices ri
                LEFT JOIN PaymentSums ps ON ri.Id = ps.InvoiceId
                LEFT JOIN Tenants t ON ri.TenantId = t.TenantId
                WHERE ri.StatusId IN (2, 3, 4, 8)  -- (unpaid, pending, inprocess, partial)
            	AND ri.TenantId = @TenantId
            )
            SELECT *
            FROM InvoiceWithTotals
            ORDER BY InvoiceDate desc;
            ";

            var parameters = new[] { new SqlParameter("@TenantId", tenantId) };
            return await _dbHelper.ExecuteQueryAsync(query, parameters);
        }

        //public async Task<int> CreateRentAsync(Payments payment, SqlConnection conn, SqlTransaction transaction)
        //{
        //    string query = @"
        //    INSERT INTO Payments (TenantId, PaymentAmount, PaymentDate, RentInvoiceId, PaymentMethod, Notes, DiscountAmount, DiscountPercent, IsLateFeeWaived, CreatedBy, CreatedAt)
        //    VALUES (@TenantId, @PaymentAmount, @PaymentDate, @RentInvoiceId, @PaymentMethod, @Notes, @DiscountAmount, @DiscountPercent, @IsLateFeeWaived, @CreatedBy, GETDATE());";

        //    var parameters = new[]
        //    {
        //        // Non-nullable
        //        new SqlParameter("@TenantId", payment.TenantId),
        //        new SqlParameter("@PaymentAmount", payment.PaymentAmount),
        //        new SqlParameter("@PaymentDate", payment.PaymentDate),
        //        new SqlParameter("@RentInvoiceId", payment.RentInvoiceId),
        //        new SqlParameter("@PaymentMethod", payment.RentInvoiceId),
        //        new SqlParameter("@Notes", string.IsNullOrWhiteSpace(payment.Notes) ? (object)DBNull.Value : payment.Notes),
        //        new SqlParameter("@DiscountAmount", payment.DiscountAmount),
        //        new SqlParameter("@DiscountPercent", payment.DiscountPercent),
        //        new SqlParameter("@IsLateFeeWaived", payment.IsLateFeeWaived),

        //        // Non-nullable
        //        new SqlParameter("@CreatedBy", payment.CreatedBy),
        //    };

        //    return await _dbHelper.ExecuteCommandAsync(query, parameters, conn, transaction);
        //}

        public async Task<int> CreatePaymentAdjustmentAsync(Payments payment, int userId)
        {
            string query = @"
            INSERT INTO Payments
                (TenantId, PaymentAmount, PaymentDate, RentInvoiceId, PaymentMethod, Notes,
                    DiscountAmount, DiscountPercent, IsLateFeeWaived, CreatedBy, CreatedAt)
                VALUES
                (@TenantId, @PaymentAmount, GETDATE(), @RentInvoiceId, @PaymentMethod, @Notes,
                    @DiscountAmount, @DiscountPercent, @IsLateFeeWaived, @CreatedBy, GETDATE()); ";

            var parameters = new[]
            {
                new SqlParameter("@TenantId", payment.TenantId),
                new SqlParameter("@PaymentAmount", payment.PaymentAmount),
                new SqlParameter("@RentInvoiceId", payment.RentInvoiceId),
                new SqlParameter("@PaymentMethod", payment.PaymentMethod),
                new SqlParameter("@Notes", payment.Notes),
                new SqlParameter("@DiscountAmount", payment.DiscountAmount),
                new SqlParameter("@DiscountPercent", payment.DiscountPercent),
                new SqlParameter("@IsLateFeeWaived", payment.IsLateFeeWaived),
                new SqlParameter("@CreatedBy", userId),
            };

            return await _dbHelper.ExecuteCommandAsync(query, parameters);
        }

        public async Task<int> CreateRentAsync(Payments payment, int userId, SqlConnection conn, SqlTransaction transaction)
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
                new SqlParameter("@PaymentMethod", payment.PaymentMethod),
                new SqlParameter("@Notes", string.IsNullOrWhiteSpace(payment.Notes) ? (object)DBNull.Value : payment.Notes),
                new SqlParameter("@DiscountAmount", payment.DiscountAmount),
                new SqlParameter("@DiscountPercent", payment.DiscountPercent),
                new SqlParameter("@IsLateFeeWaived", payment.IsLateFeeWaived),
                new SqlParameter("@CreatedBy", userId),
            };

            return await _dbHelper.ExecuteCommandAsync(query, parameters, conn, transaction);
        }

        public async Task<int> UpdateInvoiceAfterRentAsync(int invoiceId, decimal paymentAmount, SqlConnection conn, SqlTransaction transaction)
        {
            string query = @"
            UPDATE RentInvoices
            SET 
                PendingAmount = Calc.NewPending,
                OverPaidAmount = Calc.Overpaid,
                StatusId = CASE
                               WHEN Calc.NewPending = 0 AND Calc.Overpaid > 0 THEN 9  -- Overpaid
                               WHEN Calc.NewPending = 0 AND Calc.Overpaid = 0 THEN 1  -- Paid exactly
                               WHEN Calc.NewPending > 0 AND Calc.NewPending < TotalRent THEN 8  -- Partial
                               WHEN Calc.NewPending = PendingAmount THEN 2  -- Unpaid (no payment made)
                               ELSE 8  -- Fallback partial if something unexpected
                           END
            FROM RentInvoices
            CROSS APPLY (
                SELECT 
                    CASE 
                        WHEN PendingAmount - @PaymentAmount < 0 THEN 0 
                        ELSE PendingAmount - @PaymentAmount 
                    END AS NewPending,
                    CASE 
                        WHEN PendingAmount - @PaymentAmount < 0 THEN @PaymentAmount - PendingAmount 
                        ELSE 0 
                    END AS Overpaid
            ) AS Calc
            WHERE Id = @InvoiceId;
            ";

            var parameters = new[]
            {
                new SqlParameter("@InvoiceId", invoiceId),
                new SqlParameter("@PaymentAmount", paymentAmount)
            };

            return await _dbHelper.ExecuteCommandAsync(query, parameters, conn, transaction);
        }

        //public async Task<int> AddPaymentAsync(Payments rr)
        //{
        //    string query = @"
        //INSERT INTO Payments 
        //    (TenantId, PaymentAmount, PaymentDate, StatusId, RentInvoiceId, PaymentMethod, CreatedBy, CreatedAt, Notes)
        //VALUES 
        //    (@TenantId, @PaymentAmount, @PaymentDate, @StatusId, @InvoiceId, @PaymentMethod, @CreatedBy, GETDATE(), @Notes);";

        //    var parameters = new[]
        //    {
        //        new SqlParameter("@TenantId", rr.TenantId),
        //        new SqlParameter("@PaymentAmount", rr.PaymentAmount),
        //        new SqlParameter("@PaymentDate", rr.PaymentDate),
        //        new SqlParameter("@StatusId", rr.StatusId),
        //        new SqlParameter("@InvoiceId", rr.RentInvoiceId),
        //        new SqlParameter("@PaymentMethod", string.IsNullOrWhiteSpace(rr.PaymentMethod) ? (object)DBNull.Value : rr.PaymentMethod),
        //        new SqlParameter("@CreatedBy", rr.CreatedBy),
        //        new SqlParameter("@Notes", string.IsNullOrWhiteSpace(rr.Notes) ? (object)DBNull.Value : rr.Notes),
        //    };

        //    return await _dbHelper.ExecuteCommandAsync(query, parameters);
        //}

        //public async Task<int> UpdatePaymentAsync(Payments rr)
        //{
        //    string query = @"
        //    UPDATE Payments
        //    SET 
        //        TenantId = @TenantId,
        //        PaymentAmount = @PaymentAmount,
        //        PaymentDate = @PaymentDate,
        //        StatusId = @StatusId,
        //        RentInvoiceId = @RentInvoiceId,
        //        PaymentMethod = @PaymentMethod,
        //        Notes = @Notes,
        //        UpdatedBy = @UpdatedBy,
        //        UpdatedAt = GETDATE()
        //    WHERE Id = @Id;";

        //    var parameters = new[]
        //    {
        //        new SqlParameter("@TenantId", rr.TenantId),
        //        new SqlParameter("@PaymentAmount", rr.PaymentAmount),
        //        new SqlParameter("@PaymentDate", rr.PaymentDate),
        //        new SqlParameter("@StatusId", rr.StatusId),
        //        new SqlParameter("@RentInvoiceId", rr.RentInvoiceId),
        //        new SqlParameter("@PaymentMethod", string.IsNullOrWhiteSpace(rr.PaymentMethod) ? (object)DBNull.Value : rr.PaymentMethod),
        //        new SqlParameter("@Notes", string.IsNullOrWhiteSpace(rr.Notes) ? (object)DBNull.Value : rr.Notes),
        //        new SqlParameter("@UpdatedBy", rr.UpdatedBy),
        //        new SqlParameter("@Id", rr.Id),
        //    };


        //    return await _dbHelper.ExecuteCommandAsync(query, parameters);
        //}

        //public async Task<int> UpdateInvoiceAfterPaymentAsync(int invoiceId, decimal PaymentAmount)
        //{
        //    string query = @"
        //UPDATE RentInvoices
        //SET PendingAmount = PendingAmount - @PaymentAmount,
        //    Status = CASE
        //                WHEN PendingAmount - @PaymentAmount = 0 THEN 'Paid'
        //                WHEN PendingAmount -    @PaymentAmount < TotalRent THEN 'Partial'
        //                ELSE 'Unpaid'
        //             END
        //WHERE Id = @InvoiceId;";

        //    var parameters = new[]
        //    {
        //        new SqlParameter("@InvoiceId", invoiceId),
        //        new SqlParameter("@PaymentAmount", PaymentAmount)
        //    };

        //    return await _dbHelper.ExecuteCommandAsync(query, parameters);
        //}

        //public async Task<DataTable> GetRentCollectionAsync()
        //{
        //    string query = @"
        //                SELECT
        //        ri.Id AS InvoiceId,
        //        t.TenantId,
        //        t.Name AS TenantName,
        //        b.BuildingName,
        //        f.FloorNumber,
        //        u.UnitNumber,
        //        ri.InvoiceDate,
        //        ISNULL(ri.TotalRent, 0) AS MonthlyRent,
        //        ri.DueDate,

        //        ISNULL(ri.TotalRent - SUM(ISNULL(p.PaymentAmount, 0) + ISNULL(p.DiscountAmount, 0)), 0) 
        //            AS RemainingAmount,

        //        -- LateFee: Only apply if NOT waived on ANY payment for this invoice
        //        -- (or change logic to: waived only if ALL payments waived, etc.)
        //        CASE 
        //            WHEN ISNULL(ri.TotalRent - SUM(ISNULL(p.PaymentAmount, 0) + ISNULL(p.DiscountAmount, 0)), 0) > 0
        //                 AND ri.DueDate < CAST(GETUTCDATE() AS DATE)

        //                 -- Check if late fee was EVER waived for this invoice
        //                 AND NOT EXISTS (
        //                     SELECT 1 
        //                     FROM Payments p2 
        //                     WHERE p2.RentInvoiceId = ri.Id 
        //                       AND ISNULL(p2.IsLateFeeWaived, 0) = 1
        //                 )
        //            THEN dbo.CalculateLateFee(
        //                     ISNULL(ri.TotalRent - SUM(ISNULL(p.PaymentAmount, 0) + ISNULL(p.DiscountAmount, 0)), 0),
        //                     ri.TotalRent,
        //                     ri.DueDate,
        //                     GETUTCDATE()
        //                 )
        //            ELSE 0
        //        END AS LateFee,

        //        s.StatusName,
        //        SUM(ISNULL(p.PaymentAmount, 0)) AS PaidAmount,
        //        SUM(ISNULL(p.DiscountAmount, 0)) AS AppliedDiscount,
        //        MAX(p.PaymentDate) AS LastPaymentDate

        //    FROM RentInvoices ri
        //    INNER JOIN Tenants t ON ri.TenantId = t.TenantId
        //    INNER JOIN Units u ON t.UnitId = u.UnitId
        //    INNER JOIN Floors f ON u.FloorId = f.FloorId
        //    INNER JOIN Buildings b ON u.BuildingId = b.BuildingId
        //    INNER JOIN StatusList s ON ri.StatusId = s.StatusId
        //    LEFT JOIN Payments p ON ri.Id = p.RentInvoiceId

        //    WHERE ri.StatusId IN (2, 8, 9)

        //    GROUP BY
        //        ri.Id, t.TenantId, t.Name, b.BuildingName, f.FloorNumber, u.UnitNumber,
        //        ri.InvoiceDate, ri.TotalRent, ri.DueDate, s.StatusName

        //    ORDER BY ri.InvoiceDate DESC, t.Name;
        //    ";
        //    return await _dbHelper.ExecuteQueryAsync(query);
        //}

        public async Task<DataTable> GetRentCollectionAsync()
        {
            string query = @"
            SELECT
                ri.Id AS InvoiceId,
                t.TenantId,
                t.Name AS TenantName,
                b.BuildingName,
                f.FloorNumber,
                u.UnitNumber,
                ri.InvoiceDate,
                ISNULL(ri.TotalRent, 0) AS MonthlyRent,
                ri.DueDate,
                
                -- Correct & consistent RemainingAmount (same as history query)
                ISNULL(ri.TotalRent - SUM(ISNULL(p.PaymentAmount, 0) + ISNULL(p.DiscountAmount, 0)), 0) 
                    AS RemainingAmount,
                
                -- Late Fee: Simple, consistent, and safe (no scalar function needed)
                CASE 
                    WHEN ISNULL(ri.TotalRent - SUM(ISNULL(p.PaymentAmount, 0) + ISNULL(p.DiscountAmount, 0)), 0) > 0
                         AND ri.DueDate < CAST(GETUTCDATE() AS DATE)
                         AND MAX(CASE WHEN ISNULL(p.IsLateFeeWaived, 0) = 1 THEN 1 ELSE 0 END) = 0  -- No waiver in any payment
                    THEN ROUND(
                             (ISNULL(ri.TotalRent - SUM(ISNULL(p.PaymentAmount, 0) + ISNULL(p.DiscountAmount, 0)), 0)) * 0.05, 
                             0
                         )
                    ELSE 0
                END AS LateFee,
                
                s.StatusName,
                SUM(ISNULL(p.PaymentAmount, 0)) AS PaidAmount,
                SUM(ISNULL(p.DiscountAmount, 0)) AS AppliedDiscount,
                MAX(p.PaymentDate) AS LastPaymentDate
            
            FROM RentInvoices ri
            INNER JOIN Tenants t ON ri.TenantId = t.TenantId
            INNER JOIN Units u ON t.UnitId = u.UnitId
            INNER JOIN Floors f ON u.FloorId = f.FloorId
            INNER JOIN Buildings b ON u.BuildingId = b.BuildingId
            INNER JOIN StatusList s ON ri.StatusId = s.StatusId
            LEFT JOIN Payments p ON ri.Id = p.RentInvoiceId
            
            WHERE ri.StatusId IN (2, 8, 9)
            
            GROUP BY
                ri.Id, t.TenantId, t.Name, b.BuildingName, f.FloorNumber, u.UnitNumber,
                ri.InvoiceDate, ri.TotalRent, ri.DueDate, s.StatusName
            
            ORDER BY ri.InvoiceDate DESC, t.Name;
        ";

            return await _dbHelper.ExecuteQueryAsync(query);
        }

        public async Task<DataTable> GetPaymentHistoryAsync(int invoiceId)
        {
            string query = @"
        SELECT PaymentAmount, PaymentDate, PaymentMethod, StatusId
        FROM Payments
        WHERE RentInvoiceId = @InvoiceId
        ORDER BY PaymentDate ASC;";

            var parameters = new[] { new SqlParameter("@InvoiceId", invoiceId) };
            return await _dbHelper.ExecuteQueryAsync(query, parameters);
        }

        public async Task<int> BulkUpdateDueDateAsync(List<int> invoiceIds, DateTime newDueDate)
        {
            if (invoiceIds == null || invoiceIds.Count == 0)
                throw new ArgumentException("InvoiceIds cannot be empty.", nameof(invoiceIds));

            // Build parameterized IN clause
            var inParams = invoiceIds.Select((id, index) => $"@Id{index}").ToArray();
            string query = $@"
        UPDATE RentInvoices
        SET DueDate = @NewDueDate
        WHERE Id IN ({string.Join(", ", inParams)});";

            var parameters = invoiceIds
                .Select((id, index) => new SqlParameter($"@Id{index}", id))
                .ToList();
            parameters.Add(new SqlParameter("@NewDueDate", newDueDate));

            return await _dbHelper.ExecuteCommandAsync(query, parameters.ToArray());
        }

    }
}
