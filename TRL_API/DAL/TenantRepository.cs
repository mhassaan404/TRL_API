using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.Data;
using TRL_API.Models;

namespace TRL_API.DAL
{
    public class TenantRepository
    {
        private readonly DbHelper _dbHelper;

        public TenantRepository(DbHelper dbHelper)
        {
            _dbHelper = dbHelper;
        }

        public async Task<DataTable> GetTenants()
        {
            string query = @"select t.*, b.BuildingName, f.FloorNumber, u.UnitNumber, c.Name AS CityName from [dbo].[Tenants] t  LEFT JOIN Buildings b on t.BuildingId=b.BuildingId
                 LEFT JOIN Floors f on t.FloorId=f.FloorId  LEFT JOIN Units u on t.UnitId=u.UnitId LEFT JOIN City c ON t.CityId = c.Id";
            var dt = await _dbHelper.ExecuteQueryAsync(query);
            return dt;
        }

        public async Task<DataTable> GetBuildings()
        {
            string query = @"SELECT BuildingId, BuildingName FROM Buildings WHERE IsActive = 1;";
            var dt = await _dbHelper.ExecuteQueryAsync(query);
            return dt;
        }

        public async Task<DataTable> GetFloors(int? buildingId)
        {
            string query;
            var parameters = new List<SqlParameter>();

            if (buildingId.HasValue)
            {
                query = @"
                    SELECT FloorId, FloorNumber
                    FROM Floors
                    WHERE BuildingId = @BuildingId AND IsActive = 1;";
                parameters.Add(new SqlParameter("@BuildingId", buildingId.Value));
            }
            else
            {
                // No building selected → return empty result
                query = @"SELECT FloorId, FloorNumber FROM Floors WHERE 1 = 0;";
            }

            var dt = await _dbHelper.ExecuteQueryAsync(query, parameters.ToArray());
            return dt;
        }


        public async Task<DataTable> GetUnits(int? floorId)
        {
            string query;
            var parameters = new List<SqlParameter>();

            if (floorId.HasValue)
            {
                query = @"
                    SELECT UnitId, UnitNumber
                    FROM Units
                    WHERE FloorId = @FloorId AND IsActive = 1;";
                parameters.Add(new SqlParameter("@FloorId", floorId.Value));
            }
            else
            {
                // No floor selected → no rows returned
                query = @"SELECT UnitId, UnitNumber FROM Units WHERE 1 = 0;";
            }

            var dt = await _dbHelper.ExecuteQueryAsync(query, parameters.ToArray());
            return dt;
        }

        public async Task<DataTable> GetCities()
        {
            string query = @"select * from [dbo].[City]";
            var dt = await _dbHelper.ExecuteQueryAsync(query);
            return dt;
        }

        public async Task<(int Result, string ErrorMessage)> SaveTenantAsync(Tenants tenant)
        {
            try
            {
                string query = @"
                INSERT INTO [dbo].[Tenants]
                    (Name, BuildingId, FloorId, UnitId, Contact, Email, MonthlyRent, MoveOutDate, CityId, CreatedBy, CreatedAt, Notes, IsActive)
                VALUES
                    (@Name, @BuildingId, @FloorId, @UnitId, @Contact, @Email, @MonthlyRent, @MoveOutDate, @CityId, @CreatedBy, GETUTCDATE(), @Notes, @IsActive)";

                if (tenant.IsActive == true)
                    tenant.MoveOutDate = null;
                else
                    tenant.MoveOutDate = DateTime.UtcNow;

                var parameters = new[]
                {
                    new SqlParameter("@Name", tenant.Name ?? (object)DBNull.Value),
                    new SqlParameter("@BuildingId", tenant.BuildingId == 0 ? (object)DBNull.Value : tenant.BuildingId),
                    new SqlParameter("@FloorId", tenant.FloorId == 0 ? (object)DBNull.Value : tenant.FloorId),
                    new SqlParameter("@UnitId", tenant.UnitId == 0 ? (object)DBNull.Value : tenant.UnitId),
                    new SqlParameter("@Contact", tenant.Contact ?? (object)DBNull.Value),
                    new SqlParameter("@Email", tenant.Email ?? (object)DBNull.Value),
                    new SqlParameter("@MonthlyRent", tenant.MonthlyRent == 0 ? (object)DBNull.Value : tenant.MonthlyRent),
                    new SqlParameter("@MoveOutDate", tenant.MoveOutDate ?? (object)DBNull.Value),
                    new SqlParameter("@CityId", tenant.CityId == 0 ?(object) DBNull.Value : tenant.BuildingId),
                    new SqlParameter("@CreatedBy", tenant.CreatedBy == 0 ? (object)DBNull.Value : tenant.UnitId),
                    new SqlParameter("@CreatedAt", tenant.CreatedAt ?? (object)DBNull.Value),
                    new SqlParameter("@Notes", tenant.Notes ?? (object)DBNull.Value),
                    new SqlParameter("@IsActive", tenant.IsActive ?? (object)DBNull.Value)
                };

                int result = await _dbHelper.ExecuteCommandAsync(query, parameters);
                return (result, "");
            }
            catch (Exception ex)
            {
                return (0, ex.Message); // return error message
            }
        }

        public async Task<(int Result, string ErrorMessage)> UpdateTenantAsync(Tenants tenant)
        {
            try
            {
                string query = @"
            UPDATE [dbo].[Tenants]
            SET
                Name = @Name,
                BuildingId = @BuildingId,
                FloorId = @FloorId,
                UnitId = @UnitId,
                Contact = @Contact,
                Email = @Email,
                MonthlyRent = @MonthlyRent,
                MoveOutDate = @MoveOutDate,
                CityId = @CityId,
                UpdatedBy = @UpdatedBy,
                UpdatedAt = GETUTCDATE(),
                Notes=@Notes,
                IsActive = @IsActive
            WHERE TenantId = @TenantId";

                if (tenant.IsActive == true)
                    tenant.MoveOutDate = null;
                else
                    tenant.MoveOutDate = DateTime.UtcNow;

                var parameters = new[]
                {
                    new SqlParameter("@TenantId", tenant.TenantId),
                    new SqlParameter("@Name", tenant.Name ?? (object)DBNull.Value),
                    new SqlParameter("@BuildingId", tenant.BuildingId == 0 ? (object)DBNull.Value : tenant.BuildingId),
                    new SqlParameter("@FloorId", tenant.FloorId == 0 ? (object)DBNull.Value : tenant.FloorId),
                    new SqlParameter("@UnitId", tenant.UnitId == 0 ? (object)DBNull.Value : tenant.UnitId),
                    new SqlParameter("@Contact", tenant.Contact ?? (object)DBNull.Value),
                    new SqlParameter("@Email", tenant.Email ?? (object)DBNull.Value),
                    new SqlParameter("@MonthlyRent", tenant.MonthlyRent == 0 ? (object)DBNull.Value : tenant.MonthlyRent),
                    new SqlParameter("@MoveOutDate", tenant.MoveOutDate ?? (object)DBNull.Value),
                    new SqlParameter("@CityId", tenant.CityId == 0 ? (object)DBNull.Value : tenant.CityId),
                    new SqlParameter("@UpdatedBy", tenant.UpdatedBy == 0 ? (object)DBNull.Value : tenant.UpdatedBy),
                    new SqlParameter("@UpdatedAt", tenant.UpdatedAt ?? (object)DBNull.Value),
                    new SqlParameter("@Notes", tenant.Notes ?? (object)DBNull.Value),
                    new SqlParameter("@IsActive", tenant.IsActive ?? (object)DBNull.Value)
                };

                int result = await _dbHelper.ExecuteCommandAsync(query, parameters);
                return (result, "");
            }
            catch (Exception ex)
            {
                return (0, ex.Message); // return error message
            }
        }

        public async Task<(int Result, string ErrorMessage)> DeleteTenantAsync(int tenantId)
        {
            try
            {
                string query = @"DELETE FROM [dbo].[Tenants] WHERE TenantId = @TenantId";

                var parameters = new[]
                {
                    new SqlParameter("@TenantId", tenantId),
                };

                int result = await _dbHelper.ExecuteCommandAsync(query, parameters);
                return (result, "");
            }
            catch (Exception ex)
            {
                return (0, ex.Message); // return error message
            }
        }

    }
}
