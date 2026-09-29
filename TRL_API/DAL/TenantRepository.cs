using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.Data;
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
            string query = @"select t.*, b.BuildingName, f.FloorNumber, u.UnitNumber, c.Name AS CityName from [dbo].[Tenants] t  LEFT JOIN Buildings b on t.BuildingId=b.BuildingId
                 LEFT JOIN Floors f on t.FloorId=f.FloorId  LEFT JOIN Units u on t.UnitId=u.UnitId LEFT JOIN City c ON t.CityId = c.Id
                 WHERE t.IsDeleted = 0";
            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(query);
            return dt;
        }

        public async Task<ApiResponse> SaveTenantAsync(Tenants tenant)
        {
            if (tenant.IsActive == true)
                tenant.MoveOutDate = null;
            else
                tenant.MoveOutDate = DateTime.UtcNow;

            string query = @"
                INSERT INTO [dbo].[Tenants]
                    (Name, BuildingId, FloorId, UnitId, Contact, Email, MonthlyRent, MoveOutDate, CityId, CreatedBy, CreatedAt, Notes, IsActive)
                VALUES
                    (@Name, @BuildingId, @FloorId, @UnitId, @Contact, @Email, @MonthlyRent, @MoveOutDate, @CityId, @CreatedBy, GETUTCDATE(), @Notes, @IsActive)";

            var parameters = new[]
            {
                new SqlParameter("@Name", tenant.Name ?? (object)DBNull.Value),
                new SqlParameter("@BuildingId", tenant.BuildingId == 0 ? (object)DBNull.Value : tenant.BuildingId),
                new SqlParameter("@FloorId", tenant.FloorId == 0 ? (object)DBNull.Value : tenant.FloorId),
                new SqlParameter("@UnitId", tenant.UnitId == 0 ? (object)DBNull.Value : tenant.UnitId),
                new SqlParameter("@Contact", tenant.Contact ?? (object)DBNull.Value),
                new SqlParameter("@Email", tenant.Email ?? (object)DBNull.Value),
                new SqlParameter("@MonthlyRent", tenant.MonthlyRent is null or 0 ? (object)DBNull.Value : tenant.MonthlyRent),
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
            string query = @"
            UPDATE [dbo].[Tenants]
            SET
                Name = @Name,
                Contact = @Contact,
                Email = @Email,
                -- Set only when the tenant goes from active to inactive; kept on later edits; cleared on reactivation
                MoveOutDate = CASE WHEN @IsActive = 1 THEN NULL
                                   WHEN ISNULL(IsActive, 0) = 1 OR MoveOutDate IS NULL THEN GETUTCDATE()
                                   ELSE MoveOutDate END,
                CityId = @CityId,
                UpdatedBy = @UpdatedBy,
                UpdatedAt = GETUTCDATE(),
                Notes=@Notes,
                IsActive = @IsActive
            WHERE TenantId = @TenantId AND IsDeleted = 0";

            var parameters = new[]
            {
                new SqlParameter("@TenantId", tenant.TenantId),
                new SqlParameter("@Name", tenant.Name ?? (object)DBNull.Value),
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

        public async Task<ApiResponse> DeleteTenantAsync(int tenantId, int userId)
        {
            // Soft delete: keeps the tenant's invoices/payments intact. The tenant is also moved out so it
            // stops being invoiced and frees its unit.
            string query = @"
                UPDATE [dbo].[Tenants]
                SET IsDeleted = 1, IsActive = 0, MoveOutDate = ISNULL(MoveOutDate, GETUTCDATE()),
                    UpdatedBy = @UpdatedBy, UpdatedAt = GETUTCDATE()
                WHERE TenantId = @TenantId AND IsDeleted = 0";

            var parameters = new[]
            {
                new SqlParameter("@TenantId", tenantId),
                new SqlParameter("@UpdatedBy", userId),
            };

            return await _dbHelper.ExecuteQueryAsync(query, parameters);
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
