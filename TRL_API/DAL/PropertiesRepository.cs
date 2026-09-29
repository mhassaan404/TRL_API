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
                u.Note
            FROM Buildings b
            INNER JOIN Floors f ON f.BuildingId = b.BuildingId
            INNER JOIN Units u ON u.FloorId = f.FloorId
            INNER JOIN City c ON b.CityId = c.Id
            INNER JOIN UnitStatus us ON u.StatusId = us.Id
            WHERE u.IsActive = 1
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

        public async Task<ApiResponse> DeleteBuilding(int buildingId)
        {
            string query = @"UPDATE Buildings SET IsActive = 0 WHERE BuildingId = @BuildingId";

            var parameters = new[]
            {
                new SqlParameter("@BuildingId", buildingId)
            };

            return await _dbHelper.ExecuteQueryAsync(query, parameters);
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

        public async Task<ApiResponse> DeleteFloor(int floorId)
        {
            string query = @"UPDATE Floors SET IsActive = 0 WHERE FloorId = @FloorId";

            var parameters = new[]
            {
                new SqlParameter("@FloorId", floorId)
            };

            return await _dbHelper.ExecuteQueryAsync(query, parameters);
        }

        // ---------------- UNIT ----------------

        public async Task<ApiResponse> SaveUnit(int floorId, int buildingId, string unitNumber,
            int statusId, double baseRent, string? note, string? propertyType)
        {
            string query = @"INSERT INTO Units (
                FloorId, BuildingId, UnitNumber, StatusId, BaseRent, Note, PropertyType, IsActive
            )
            OUTPUT INSERTED.UnitId
            VALUES (
                @FloorId, @BuildingId, @UnitNumber, @StatusId, @BaseRent, @Note, @PropertyType, 1
            )";

            var parameters = new[]
            {
                new SqlParameter("@FloorId", floorId),
                new SqlParameter("@BuildingId", buildingId),
                new SqlParameter("@UnitNumber", unitNumber),
                new SqlParameter("@StatusId", statusId),
                new SqlParameter("@BaseRent", baseRent),
                new SqlParameter("@Note", (object?)note ?? DBNull.Value),
                new SqlParameter("@PropertyType", (object?)propertyType ?? DBNull.Value)
            };

            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);

            return dt.Rows.Count > 0
                ? new ApiResponse { IsSuccess = true, Id = Convert.ToInt32(dt.Rows[0]["UnitId"]) }
                : new ApiResponse { IsSuccess = false, Message = "Failed to save unit." };
        }

        public async Task<ApiResponse> UpdateUnit(int unitId, int floorId, int buildingId,
            string unitNumber, int statusId, double baseRent, string? note, string? propertyType)
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
            WHERE UnitId = @UnitId";

            var parameters = new[]
            {
                new SqlParameter("@UnitId", unitId),
                new SqlParameter("@FloorId", floorId),
                new SqlParameter("@BuildingId", buildingId),
                new SqlParameter("@UnitNumber", unitNumber),
                new SqlParameter("@StatusId", statusId),
                new SqlParameter("@BaseRent", baseRent),
                new SqlParameter("@Note", (object?)note ?? DBNull.Value),
                new SqlParameter("@PropertyType", (object?)propertyType ?? DBNull.Value)
            };

            return await _dbHelper.ExecuteQueryAsync(query, parameters);
        }

        public async Task<ApiResponse> DeleteUnit(int unitId)
        {
            string query = @"UPDATE Units SET IsActive = 0 WHERE UnitId = @UnitId";

            var parameters = new[]
            {
                new SqlParameter("@UnitId", unitId)
            };

            return await _dbHelper.ExecuteQueryAsync(query, parameters);
        }
    }
}