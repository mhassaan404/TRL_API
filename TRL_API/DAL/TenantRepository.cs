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
            string query = @"select t.*, b.BuildingName, f.FloorNumber, u.UnitNumber from [dbo].[Tenants] t left join Buildings b on t.BuildingId=b.BuildingId
                left join Floors f on t.FloorId=f.FloorId left join Units u on t.UnitId=u.UnitId";
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

        public async Task<DataTable> GetUnits()
        {
            string query = @"select * from [dbo].[Tenants]";
            var dt = await _dbHelper.ExecuteQueryAsync(query);
            return dt;
        }

        public async Task<int> SaveTenantAsync(Tenants tenant)
        {
            string query = @"
                INSERT INTO [dbo].[Tenants]
                    (Name, BuildingId, FloorId, UnitId, Contact, Email, MonthlyRent, MoveOutDate, City, CreatedBy, CreatedAt, IsActive)
                VALUES
                    (@Name, @BuildingId, @FloorId, @UnitId, @Contact, @Email, @MonthlyRent, @MoveOutDate, @City, @CreatedBy, GETUTCDATE(), @IsActive)";

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
                new SqlParameter("@City", tenant.City ?? (object)DBNull.Value),
                new SqlParameter("@CreatedBy", tenant.CreatedBy == 0 ? (object)DBNull.Value : tenant.UnitId),
                new SqlParameter("@IsActive", tenant.IsActive ?? (object)DBNull.Value)
            };

            int result = await _dbHelper.ExecuteCommandAsync(query, parameters);
            return result;
        }

        public async Task<int> UpdateTenantAsync(Tenants tenant)
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
                City = @City,
                UpdatedBy = @UpdatedBy,
                UpdatedAt = GETUTCDATE(),
                IsActive = @IsActive
            WHERE TenantId = @TenantId";

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
                new SqlParameter("@City", tenant.City ?? (object)DBNull.Value),
                new SqlParameter("@UpdatedBy", tenant.UpdatedBy ?? (object)DBNull.Value),
                new SqlParameter("@IsActive", tenant.IsActive ?? (object)DBNull.Value)
            };

            int result = await _dbHelper.ExecuteCommandAsync(query, parameters);
            return result;
        }

    }
}
