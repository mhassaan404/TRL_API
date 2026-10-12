using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.Data;
using TRL_API.Models;

namespace TRL_API.DAL
{
    public class PropertiesRepository : IPropertiesRepository
    {
        private readonly DbHelper _dbHelper;

        public PropertiesRepository(DbHelper dbHelper)
        {
            _dbHelper = dbHelper;
        }

        public async Task<DataTable> GetProperties()
        {
            string query = @"SELECT 
                u.BuildingId,
                u.UnitId,
                b.BuildingName,
                f.FloorId,
                f.FloorNumber,
                u.UnitNumber,
                u.BaseRent,
                u.PropertyType,
                c.Name AS CityName,
                us.Id AS StatusId,
                us.Name AS Status,
                u.Note,
                -- Occupied = the unit has an active lease (or an active tenant still points at it)
                CAST(CASE WHEN EXISTS (SELECT 1 FROM TenantLeases tl WHERE tl.UnitId = u.UnitId AND tl.IsActive = 1)
                            OR EXISTS (SELECT 1 FROM Tenants t WHERE t.UnitId = u.UnitId AND t.IsActive = 1 AND t.IsDeleted = 0)
                          THEN 1 ELSE 0 END AS BIT) AS IsOccupied
            FROM Buildings b
            INNER JOIN Floors f ON f.BuildingId = b.BuildingId
            INNER JOIN Units u ON u.FloorId = f.FloorId
            INNER JOIN City c ON b.CityId = c.Id
            INNER JOIN UnitStatus us ON u.StatusId = us.Id
            WHERE u.IsActive = 1 AND f.IsActive = 1 AND b.IsActive = 1
            ORDER BY b.BuildingName, f.FloorNumber, u.UnitNumber;";

            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query);
        }

        public async Task<DataTable> GetBuildings()
        {
            string query = @"SELECT 
                b.BuildingId, b.BuildingName, b.Address, b.CityId, b.TypeId, c.Name AS CityName, bt.Name AS Type
            FROM Buildings b
            JOIN City c ON b.CityId = c.Id
            JOIN BuildingType bt ON b.TypeId = bt.Id
            WHERE b.IsActive = 1
            ORDER BY b.BuildingName";

            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query);
        }

        public async Task<DataTable> GetFloorsByBuilding(int buildingId)
        {
            string query = @"SELECT 
                FloorId,
                FloorNumber,
                BuildingId
            FROM Floors
            WHERE BuildingId = @BuildingId
            AND IsActive = 1";

            var parameters = new[]
            {
                new SqlParameter("@BuildingId", buildingId)
            };

            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
        }

        public async Task<DataTable> GetUnitsByFloor(int floorId)
        {
            string query = @"SELECT 
                UnitId,
                UnitNumber
            FROM Units
            WHERE FloorId = @FloorId
            AND IsActive = 1";

            var parameters = new[]
            {
                new SqlParameter("@FloorId", floorId)
            };

            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
        }

        public async Task<DataTable> GetUnitsStatus()
        {
            string query = @"SELECT 
                Id,
                Name
            FROM UnitStatus
            WHERE IsActive = 1";

            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query);
        }

        public async Task<DataTable> GetCities()
        {
            string query = @"SELECT Id, Name FROM City";
            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query);
        }

        public async Task<DataTable> GetBuildingTypes()
        {
            string query = @"SELECT Id, Name FROM BuildingType";
            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query);
        }

        // ---------------- BUILDING ----------------

        public async Task<ApiResponse> SaveBuilding(string? buildingName, int cityId, int typeId, string? address)
        {
            string query = @"INSERT INTO Buildings 
                (BuildingName, CityId, TypeId, Address, IsActive)
                OUTPUT INSERTED.BuildingId
                VALUES (@BuildingName, @CityId, @TypeId, @Address, 1)";

            var parameters = new[]
            {
                new SqlParameter("@BuildingName", buildingName ?? (object)DBNull.Value),
                new SqlParameter("@CityId", cityId),
                new SqlParameter("@TypeId", typeId),
                new SqlParameter("@Address", (object?)address ?? DBNull.Value)
            };

            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);

            return dt.Rows.Count > 0
                ? new ApiResponse { IsSuccess = true, Id = Convert.ToInt32(dt.Rows[0]["BuildingId"]) }
                : new ApiResponse { IsSuccess = false, Message = "Failed to save building." };
        }

        public async Task<ApiResponse> UpdateBuilding(int buildingId, string? buildingName, int cityId, int typeId, string? address)
        {
            string query = @"UPDATE Buildings
                SET BuildingName = @BuildingName,
                    CityId = @CityId,
                    TypeId = @TypeId,
                    Address = @Address
                WHERE BuildingId = @BuildingId";

            var parameters = new[]
            {
                new SqlParameter("@BuildingId", buildingId),
                new SqlParameter("@BuildingName", buildingName ?? (object)DBNull.Value),
                new SqlParameter("@CityId", cityId),
                new SqlParameter("@TypeId", typeId),
                new SqlParameter("@Address", (object?)address ?? DBNull.Value)
            };

            return await _dbHelper.ExecuteQueryAsync(query, parameters);
        }

        public async Task<string> DeleteBuilding(int buildingId)
        {
            // Soft delete, only when nothing still uses it; the row is locked first so a lease or unit can't be added
            // in between. Result: OK, IN_USE or NOT_FOUND (missing or already deleted).
            string query = @"
                SET XACT_ABORT ON;
                DECLARE @r VARCHAR(10), @Found INT;
                BEGIN TRAN;
                SELECT @Found = BuildingId FROM Buildings WITH (UPDLOCK, HOLDLOCK) WHERE BuildingId = @BuildingId AND IsActive = 1;
                IF @Found IS NULL SET @r = 'NOT_FOUND';
                ELSE IF EXISTS (SELECT 1 FROM Floors WHERE BuildingId = @BuildingId AND IsActive = 1)
                     OR EXISTS (SELECT 1 FROM Units WHERE BuildingId = @BuildingId AND IsActive = 1) SET @r = 'IN_USE';
                ELSE BEGIN UPDATE Buildings SET IsActive = 0 WHERE BuildingId = @BuildingId; SET @r = 'OK'; END
                COMMIT;
                SELECT @r AS Result;";
            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(query, new[] { new SqlParameter("@BuildingId", buildingId) });
            return dt.Rows[0]["Result"].ToString()!;
        }

        // ---------------- FLOOR ----------------

        public async Task<ApiResponse> SaveFloor(int buildingId, string floorNumber)
        {
            string query = @"INSERT INTO Floors 
                (BuildingId, FloorNumber, IsActive)
                OUTPUT INSERTED.FloorId
                VALUES (@BuildingId, @FloorNumber, 1)";

            var parameters = new[]
            {
                new SqlParameter("@BuildingId", buildingId),
                new SqlParameter("@FloorNumber", floorNumber)
            };

            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);

            return dt.Rows.Count > 0
                ? new ApiResponse { IsSuccess = true, Id = Convert.ToInt32(dt.Rows[0]["FloorId"]) }
                : new ApiResponse { IsSuccess = false, Message = "Failed to save floor." };
        }

        public async Task<ApiResponse> UpdateFloor(int floorId, string floorNumber)
        {
            string query = @"UPDATE Floors SET FloorNumber = @FloorNumber WHERE FloorId = @FloorId";

            var parameters = new[]
            {
                new SqlParameter("@FloorId", floorId),
                new SqlParameter("@FloorNumber", floorNumber)
            };

            return await _dbHelper.ExecuteQueryAsync(query, parameters);
        }

        public async Task<string> DeleteFloor(int floorId)
        {
            // Soft delete, only when nothing still uses it; the row is locked first so a lease or unit can't be added
            // in between. Result: OK, IN_USE or NOT_FOUND (missing or already deleted).
            string query = @"
                SET XACT_ABORT ON;
                DECLARE @r VARCHAR(10), @Found INT;
                BEGIN TRAN;
                SELECT @Found = FloorId FROM Floors WITH (UPDLOCK, HOLDLOCK) WHERE FloorId = @FloorId AND IsActive = 1;
                IF @Found IS NULL SET @r = 'NOT_FOUND';
                ELSE IF EXISTS (SELECT 1 FROM Units WHERE FloorId = @FloorId AND IsActive = 1) SET @r = 'IN_USE';
                ELSE BEGIN UPDATE Floors SET IsActive = 0 WHERE FloorId = @FloorId; SET @r = 'OK'; END
                COMMIT;
                SELECT @r AS Result;";
            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(query, new[] { new SqlParameter("@FloorId", floorId) });
            return dt.Rows[0]["Result"].ToString()!;
        }

        // ---------------- UNIT ----------------

        public async Task<bool> FloorBelongsToBuildingAsync(int floorId, int buildingId)
        {
            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(
                "SELECT 1 FROM Floors WHERE FloorId = @FloorId AND BuildingId = @BuildingId AND IsActive = 1",
                new[] { new SqlParameter("@FloorId", floorId), new SqlParameter("@BuildingId", buildingId) });
            return dt.Rows.Count > 0;
        }

        public async Task<ApiResponse> SaveUnit(int floorId, int buildingId, string unitNumber,
            int statusId, decimal baseRent, string? note, string? propertyType)
        {
            // Only on an active floor of that building (checked again here in case it was deleted meanwhile)
            string query = @"INSERT INTO Units (
                FloorId, BuildingId, UnitNumber, StatusId, BaseRent, Note, PropertyType, IsActive
            )
            OUTPUT INSERTED.UnitId
            SELECT @FloorId, @BuildingId, @UnitNumber, @StatusId, @BaseRent, @Note, @PropertyType, 1
            WHERE EXISTS (SELECT 1 FROM Floors WHERE FloorId = @FloorId AND BuildingId = @BuildingId AND IsActive = 1)";

            var parameters = new[]
            {
                new SqlParameter("@FloorId", floorId),
                new SqlParameter("@BuildingId", buildingId),
                new SqlParameter("@UnitNumber", unitNumber),
                new SqlParameter("@StatusId", statusId),
                new SqlParameter("@BaseRent", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = baseRent },
                new SqlParameter("@Note", (object?)note ?? DBNull.Value),
                new SqlParameter("@PropertyType", (object?)propertyType ?? DBNull.Value)
            };

            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);

            return dt.Rows.Count > 0
                ? new ApiResponse { IsSuccess = true, Id = Convert.ToInt32(dt.Rows[0]["UnitId"]) }
                : new ApiResponse { IsSuccess = false, Message = "Failed to save unit." };
        }

        public async Task<ApiResponse> UpdateUnit(int unitId, int floorId, int buildingId,
            string unitNumber, int statusId, decimal baseRent, string? note, string? propertyType)
        {
            string query = @"UPDATE Units
            SET 
                FloorId = @FloorId,
                BuildingId = @BuildingId,
                UnitNumber = @UnitNumber,
                StatusId = @StatusId,
                BaseRent = @BaseRent,
                Note = @Note,
                PropertyType = @PropertyType
            WHERE UnitId = @UnitId
              AND EXISTS (SELECT 1 FROM Floors WHERE FloorId = @FloorId AND BuildingId = @BuildingId AND IsActive = 1)";

            var parameters = new[]
            {
                new SqlParameter("@UnitId", unitId),
                new SqlParameter("@FloorId", floorId),
                new SqlParameter("@BuildingId", buildingId),
                new SqlParameter("@UnitNumber", unitNumber),
                new SqlParameter("@StatusId", statusId),
                new SqlParameter("@BaseRent", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = baseRent },
                new SqlParameter("@Note", (object?)note ?? DBNull.Value),
                new SqlParameter("@PropertyType", (object?)propertyType ?? DBNull.Value)
            };

            return await _dbHelper.ExecuteQueryAsync(query, parameters);
        }

        public async Task<string> DeleteUnit(int unitId)
        {
            // Soft delete, only when nothing still uses it; the row is locked first so a lease or unit can't be added
            // in between. Result: OK, IN_USE or NOT_FOUND (missing or already deleted).
            string query = @"
                SET XACT_ABORT ON;
                DECLARE @r VARCHAR(10), @Found INT;
                BEGIN TRAN;
                SELECT @Found = UnitId FROM Units WITH (UPDLOCK, HOLDLOCK) WHERE UnitId = @UnitId AND IsActive = 1;
                IF @Found IS NULL SET @r = 'NOT_FOUND';
                ELSE IF EXISTS (SELECT 1 FROM TenantLeases WHERE UnitId = @UnitId AND IsActive = 1) SET @r = 'IN_USE';
                ELSE BEGIN UPDATE Units SET IsActive = 0 WHERE UnitId = @UnitId; SET @r = 'OK'; END
                COMMIT;
                SELECT @r AS Result;";
            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(query, new[] { new SqlParameter("@UnitId", unitId) });
            return dt.Rows[0]["Result"].ToString()!;
        }
    }
}