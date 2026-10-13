using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.Data;
using TRL_API.Helpers;
using TRL_API.Models;

namespace TRL_API.DAL
{
    public class TenantRepository : ITenantRepository
    {
        private readonly DbHelper _dbHelper;

        public TenantRepository(DbHelper dbHelper)
        {
            _dbHelper = dbHelper;
        }

        public async Task<DataTable> GetTenants()
        {
            // Unit, floor, building and rent are those of the tenant's current lease (dbo.TenantCurrentLease), not a copy
            string query = @"
                SELECT t.TenantId, t.Name, t.TenantType, t.ContactPerson, t.CnicNtn, t.Address, t.Contact, t.Email, t.CityId,
                       t.MoveOutDate, t.Notes, t.IsActive, t.IsDeleted, t.CreatedBy, t.CreatedAt, t.UpdatedBy, t.UpdatedAt,
                       b.BuildingId, f.FloorId, cl.UnitId, cl.RentAmount AS MonthlyRent,
                       b.BuildingName, f.FloorNumber, u.UnitNumber, c.Name AS CityName
                FROM [dbo].[Tenants] t
                OUTER APPLY dbo.TenantCurrentLease(t.TenantId) cl
                LEFT JOIN Units u ON u.UnitId = cl.UnitId
                LEFT JOIN Floors f ON f.FloorId = u.FloorId
                LEFT JOIN Buildings b ON b.BuildingId = f.BuildingId
                LEFT JOIN City c ON t.CityId = c.Id
                WHERE t.IsDeleted = 0";
            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(query);
            return dt;
        }

        public async Task<ApiResponse> SaveTenantAsync(Tenants tenant)
        {
            if (tenant.IsActive == true)
                tenant.MoveOutDate = null;
            else
                tenant.MoveOutDate = BusinessDate.Today;

            string query = @"
                INSERT INTO [dbo].[Tenants]
                    (Name, TenantType, ContactPerson, CnicNtn, Address, Contact, Email, MoveOutDate, CityId, CreatedBy, CreatedAt, Notes, IsActive)
                VALUES
                    (@Name, @TenantType, @ContactPerson, @CnicNtn, @Address, @Contact, @Email, @MoveOutDate, @CityId, @CreatedBy, GETUTCDATE(), @Notes, @IsActive)";

            var parameters = new[]
            {
                new SqlParameter("@Name", tenant.Name ?? (object)DBNull.Value),
                new SqlParameter("@TenantType", tenant.TenantType ?? (object)DBNull.Value),
                new SqlParameter("@ContactPerson", tenant.ContactPerson ?? (object)DBNull.Value),
                new SqlParameter("@CnicNtn", tenant.CnicNtn ?? (object)DBNull.Value),
                new SqlParameter("@Address", tenant.Address ?? (object)DBNull.Value),
                // Unit and rent come from leases only (Lease Management), never from the tenant form
                new SqlParameter("@Contact", tenant.Contact ?? (object)DBNull.Value),
                new SqlParameter("@Email", tenant.Email ?? (object)DBNull.Value),
                new SqlParameter("@MoveOutDate", tenant.MoveOutDate ?? (object)DBNull.Value),
                new SqlParameter("@CityId", tenant.CityId == 0 ? (object)DBNull.Value : tenant.CityId),
                new SqlParameter("@CreatedBy", tenant.CreatedBy == 0 ? (object)DBNull.Value : tenant.CreatedBy),
                new SqlParameter("@Notes", tenant.Notes ?? (object)DBNull.Value),
                new SqlParameter("@IsActive", tenant.IsActive ?? (object)DBNull.Value)
            };

            return await _dbHelper.ExecuteQueryAsync(query, parameters);
        }

        public async Task<ApiResponse> UpdateTenantAsync(Tenants tenant)
        {
            // The tenant's unit and rent come from leases (dbo.TenantCurrentLease): a reactivated tenant without an active
            // lease has no unit until a lease gives one
            string query = @"
            UPDATE [dbo].[Tenants]
            SET
                Name = @Name,
                TenantType = @TenantType,
                ContactPerson = @ContactPerson,
                CnicNtn = @CnicNtn,
                Address = @Address,
                Contact = @Contact,
                Email = @Email,
                -- Set only when the tenant goes from active to inactive; kept on later edits; cleared on reactivation
                MoveOutDate = CASE WHEN @IsActive = 1 THEN NULL
                                   WHEN ISNULL(IsActive, 0) = 1 OR MoveOutDate IS NULL THEN dbo.BusinessToday()
                                   ELSE MoveOutDate END,
                -- The tenant form no longer has a city: keep the stored one unless a city is sent
                CityId = COALESCE(@CityId, CityId),
                UpdatedBy = @UpdatedBy,
                UpdatedAt = GETUTCDATE(),
                Notes=@Notes,
                IsActive = @IsActive
            WHERE TenantId = @TenantId AND IsDeleted = 0";

            var parameters = new[]
            {
                new SqlParameter("@TenantId", tenant.TenantId),
                new SqlParameter("@Name", tenant.Name ?? (object)DBNull.Value),
                new SqlParameter("@TenantType", tenant.TenantType ?? (object)DBNull.Value),
                new SqlParameter("@ContactPerson", tenant.ContactPerson ?? (object)DBNull.Value),
                new SqlParameter("@CnicNtn", tenant.CnicNtn ?? (object)DBNull.Value),
                new SqlParameter("@Address", tenant.Address ?? (object)DBNull.Value),
                new SqlParameter("@Contact", tenant.Contact ?? (object)DBNull.Value),
                new SqlParameter("@Email", tenant.Email ?? (object)DBNull.Value),
                new SqlParameter("@CityId", tenant.CityId == 0 ? (object)DBNull.Value : tenant.CityId),
                new SqlParameter("@UpdatedBy", tenant.UpdatedBy == 0 ? (object)DBNull.Value : tenant.UpdatedBy),
                new SqlParameter("@UpdatedAt", tenant.UpdatedAt ?? (object)DBNull.Value),
                new SqlParameter("@Notes", tenant.Notes ?? (object)DBNull.Value),
                new SqlParameter("@IsActive", tenant.IsActive ?? (object)DBNull.Value)
            };

            return await _dbHelper.ExecuteQueryAsync(query, parameters);
        }

        // Soft delete: keeps the tenant's invoices/payments intact. The tenant is also moved out so it stops being
        // invoiced and frees its unit. Only when nothing is left to settle: no active lease, no money owed either way
        // on an invoice (incl. a late fee building up), no security deposit still held. The tenant row is locked first.
        // Result: OK, NOT_FOUND, HAS_LEASE, OPEN_BALANCE or DEPOSIT_HELD.
        public async Task<string> DeleteTenantAsync(int tenantId, int userId)
        {
            string query = @"
                SET XACT_ABORT ON;
                DECLARE @r VARCHAR(20), @Found INT;
                BEGIN TRAN;
                SELECT @Found = TenantId FROM Tenants WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = @TenantId AND IsDeleted = 0;
                IF @Found IS NULL SET @r = 'NOT_FOUND';
                ELSE IF EXISTS (SELECT 1 FROM TenantLeases WHERE TenantId = @TenantId AND IsActive = 1) SET @r = 'HAS_LEASE';
                ELSE IF EXISTS (SELECT 1 FROM RentInvoices ri CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) b
                                WHERE ri.TenantId = @TenantId AND ri.StatusId <> 6 AND b.Balance + b.OpenLateFee <> 0) SET @r = 'OPEN_BALANCE';
                ELSE IF (SELECT ISNULL(SUM(Amount), 0) FROM SecurityDeposits WHERE TenantId = @TenantId) > 0 SET @r = 'DEPOSIT_HELD';
                ELSE
                BEGIN
                    UPDATE Tenants
                    SET IsDeleted = 1, IsActive = 0, MoveOutDate = ISNULL(MoveOutDate, dbo.BusinessToday()),
                        UpdatedBy = @UpdatedBy, UpdatedAt = GETUTCDATE()
                    WHERE TenantId = @TenantId;
                    SET @r = 'OK';
                END
                COMMIT;
                SELECT @r AS Result;";

            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(query, new[]
            {
                new SqlParameter("@TenantId", tenantId),
                new SqlParameter("@UpdatedBy", userId),
            });
            return dt.Rows[0]["Result"].ToString()!;
        }

        public async Task<bool> HasActiveLeaseAsync(int tenantId)
        {
            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(
                "SELECT 1 FROM TenantLeases WHERE TenantId = @TenantId AND IsActive = 1",
                new[] { new SqlParameter("@TenantId", tenantId) });
            return dt.Rows.Count > 0;
        }
    }
}
