using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.SqlClient;
using Microsoft.DotNet.Scaffolding.Shared.CodeModifier.CodeChange;
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
                SELECT TenantId, CAST(TenantId AS VARCHAR(10)) + ' | ' + Name AS DisplayName
                FROM Tenants
                WHERE IsActive = 1
                ORDER BY Name;";

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
                ri.InvoiceMonth,
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
            ORDER BY ri.InvoiceMonth DESC;";

            var parameters = new[] { new SqlParameter("@TenantId", tenantId) };
            return await _dbHelper.ExecuteQueryAsync(query, parameters);
        }

        public async Task<DataTable> GetInvoiceDetailsAsync(int invoiceId)
        {
            string query = @"
                            SELECT 
                ri.TotalRent, 
                ri.PendingAmount, 
                ri.DueDate, 
                u.UnitNumber, 
                b.BuildingName, 
                f.FloorNumber
            FROM RentInvoices ri
            INNER JOIN Tenants t ON ri.TenantId = t.TenantId
            INNER JOIN Units u ON t.UnitId = u.UnitId
            INNER JOIN Floors f ON u.FloorId = f.FloorId
            INNER JOIN Buildings b ON f.BuildingId = b.BuildingId
            WHERE ri.Id = @InvoiceId;
            ";

            var parameters = new[] { new SqlParameter("@InvoiceId", invoiceId) };
            return await _dbHelper.ExecuteQueryAsync(query, parameters);
        }

        public async Task<int> CreateRentAsync(Payments payment, SqlConnection conn, SqlTransaction transaction)
        {
            string query = @"
            INSERT INTO Payments (TenantId, PaymentAmount, PaymentDate, StatusId, RentInvoiceId, PaymentMethod, CreatedBy, CreatedAt, Notes)
            VALUES (@TenantId, @PaymentAmount, @PaymentDate, @StatusId, @RentInvoiceId, @PaymentMethod, @CreatedBy, GETDATE(), @Notes);";

            var parameters = new[]
            {
                new SqlParameter("@TenantId", payment.TenantId),
                new SqlParameter("@PaymentAmount", payment.PaymentAmount),
                new SqlParameter("@PaymentDate", payment.PaymentDate),
                new SqlParameter("@StatusId", payment.StatusId),
                new SqlParameter("@RentInvoiceId", payment.RentInvoiceId),
                new SqlParameter("@PaymentMethod", string.IsNullOrWhiteSpace(payment.PaymentMethod) ? (object)DBNull.Value : payment.PaymentMethod),
                new SqlParameter("@CreatedBy", payment.CreatedBy),
                new SqlParameter("@Notes", string.IsNullOrWhiteSpace(payment.Notes) ? (object)DBNull.Value : payment.Notes),
            };

            return await _dbHelper.ExecuteCommandAsync(query, parameters, conn, transaction);
        }

        public async Task<int> UpdateInvoiceAfterRentAsync(int invoiceId, decimal PaymentAmount, SqlConnection conn, SqlTransaction transaction)
        {
            string query = @"
            UPDATE RentInvoices
            SET PendingAmount = PendingAmount - @PaymentAmount,
                Status = CASE
                            WHEN PendingAmount - @PaymentAmount = 0 THEN 'Paid'
                            WHEN PendingAmount - @PaymentAmount < TotalRent THEN 'Partial'
                            ELSE 'Unpaid'
                         END
            WHERE Id = @InvoiceId;";

            var parameters = new[]
            {
                new SqlParameter("@InvoiceId", invoiceId),
                new SqlParameter("@PaymentAmount", PaymentAmount)
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

        public async Task<DataTable> GetRentCollectionAsync()
        {
            string query = @"WITH LatestPayments AS
            (
                SELECT *,
                       ROW_NUMBER() OVER (PARTITION BY RentInvoiceId ORDER BY PaymentDate DESC) AS rn
                FROM Payments
            )
            SELECT 
                ri.Id,
                t.Name AS TenantName,
                b.BuildingName,
                f.FloorNumber,
                u.UnitNumber,
                ri.InvoiceMonth,
                ri.TotalRent AS MonthlyRent,
                ri.PendingAmount,
                s.StatusName AS Status,
                ri.DueDate,
                p.PaymentAmount AS PaymentAmount,
                p.PaymentDate AS PaymentDate,
                p.PaymentMethod
            FROM RentInvoices ri
            INNER JOIN Tenants t ON ri.TenantId = t.TenantId
            INNER JOIN Units u ON t.UnitId = u.UnitId
            INNER JOIN Floors f ON u.FloorId = f.FloorId
            INNER JOIN Buildings b ON u.BuildingId = b.BuildingId
            INNER JOIN StatusList s ON ri.StatusId = s.StatusId
            LEFT JOIN LatestPayments p 
                   ON ri.Id = p.RentInvoiceId AND p.rn = 1
            ORDER BY ri.InvoiceMonth DESC, t.Name;
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
