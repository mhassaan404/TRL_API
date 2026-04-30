//using Microsoft.Data.SqlClient;
//using System.Data;
//using System.Net;
//using TRL_API.Data;
//using TRL_API.Models;

//namespace TRL_API.DAL
//{
//    public class PropertiesRepository
//    {
//        private readonly DbHelper _dbHelper;

//        public PropertiesRepository(DbHelper dbHelper)
//        {
//            _dbHelper = dbHelper;
//        }

//        public async Task<DataTable> GetProperties()
//        {
//            string query = @"SELECT 
//                u.UnitId,
//                b.BuildingName,
//                f.FloorNumber,
//                u.UnitNumber,
//                u.BaseRent,
//                bt.Name AS PropertyType,
//                c.Name AS CityName,
//                us.Name AS Status,
//                u.Note
//            FROM Buildings b
//            INNER JOIN Floors f ON f.BuildingId = b.BuildingId
//            INNER JOIN Units u ON u.FloorId = f.FloorId
//            INNER JOIN BuildingType bt ON b.TypeId = bt.Id
//            INNER JOIN City c ON b.CityId = c.Id
//            INNER JOIN UnitStatus us ON u.StatusId = us.Id
//            WHERE u.IsActive = 1
//            ORDER BY b.BuildingName, f.FloorNumber, u.UnitNumber;";
//            var dt = await _dbHelper.ExecuteQueryAsync(query);
//            return dt;
//        }

//        public async Task<DataTable> GetBuildings()
//        {
//            string query = @"SELECT 
//                BuildingId,
//                BuildingName
//            FROM Buildings
//            WHERE IsActive = 1
//            ORDER BY BuildingName";
//            var dt = await _dbHelper.ExecuteQueryAsync(query);
//            return dt;
//        }

//        public async Task<DataTable> GetFloorsbyBuilding(int buildingId)
//        {
//            string query = @"SELECT 
//                FloorId,
//                FloorNumber
//            FROM Floors
//            WHERE BuildingId = @BuildingId
//            AND IsActive = 1";

//            var parameters = new[] { new SqlParameter("@BuildingId", buildingId) };
//            return await _dbHelper.ExecuteQueryAsync(query, parameters);
//        }

//        public async Task<DataTable> SaveBuilding(string BuildingName, int CityId, int TypeId, string Address)
//        {
//            string query = @"INSERT INTO Buildings (BuildingName, CityId, TypeId, Address, IsActive)
//            VALUES (@BuildingName, @CityId, @TypeId, @Address, 1)";

//            var parameters = new[]
//            {
//                new SqlParameter("@BuildingName", BuildingName),
//                new SqlParameter("@CityId", CityId),
//                new SqlParameter("@TypeId", TypeId),
//                new SqlParameter("@Address", Address)
//            };
//            return await _dbHelper.ExecuteQueryAsync(query, parameters);
//        }

//        public async Task<DataTable> SaveFloor(int BuildingId, int FloorNumber)
//        {
//            string query = @"INSERT INTO Buildings (BuildingId, FloorNumber, IsActive)
//            VALUES (@BuildingId, @FloorNumber, 1)";

//            var parameters = new[]
//            {
//                new SqlParameter("@BuildingId", BuildingId),
//                new SqlParameter("@FloorNumber", FloorNumber)
//            };
//            return await _dbHelper.ExecuteQueryAsync(query, parameters);
//        }

//        public async Task<DataTable> SaveUnit(int FloorId, int BuildingId, int UnitNumber, int StatusId, double BaseRent, string Note)
//        {
//            string query = @"INSERT INTO Units (
//                FloorId,
//                BuildingId,
//                UnitNumber,
//                StatusId,
//                BaseRent,
//                Note,
//                IsActive
//            )
//            VALUES (
//                @FloorId,
//                @BuildingId,
//                @UnitNumber,
//                @StatusId,
//                @BaseRent,
//                @Note,
//                1
//            )";

//            var parameters = new[]
//            {
//                new SqlParameter("@FloorId", FloorId),
//                new SqlParameter("@BuildingId", BuildingId),
//                new SqlParameter("@UnitNumber", UnitNumber),
//                new SqlParameter("@StatusId", StatusId),
//                new SqlParameter("@BaseRent", BaseRent),
//                new SqlParameter("@Note", Note)
//            };
//            return await _dbHelper.ExecuteQueryAsync(query, parameters);
//        }

//        public async Task<DataTable> UpdateUnit(int FloorId, int BuildingId, int UnitNumber, int StatusId, double BaseRent, string Note)
//        {
//            string query = @"UPDATE Units
//            SET 
//                FloorId = @FloorId,
//                BuildingId = @BuildingId,
//                UnitNumber = @UnitNumber,
//                StatusId = @StatusId,
//                BaseRent = @BaseRent,
//                Note = @Note
//            WHERE UnitId = @UnitId";

//            var parameters = new[]
//            {
//                new SqlParameter("@FloorId", FloorId),
//                new SqlParameter("@BuildingId", BuildingId),
//                new SqlParameter("@UnitNumber", UnitNumber),
//                new SqlParameter("@StatusId", StatusId),
//                new SqlParameter("@BaseRent", BaseRent),
//                new SqlParameter("@Note", Note)
//            };
//            return await _dbHelper.ExecuteQueryAsync(query, parameters);
//        }

//        public async Task<DataTable> DeleteUnit(int UnitNumber)
//        {
//            string query = @"UPDATE Units
//            SET IsActive = 0
//            WHERE UnitId = @UnitId";

//            var parameters = new[]
//            {
//                new SqlParameter("@UnitNumber", UnitNumber)
//            };
//            return await _dbHelper.ExecuteQueryAsync(query, parameters);
//        }
//    }
//}




using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.Data;

namespace TRL_API.DAL
{
    public class PropertiesRepository
    {
        private readonly DbHelper _dbHelper;

        public PropertiesRepository(DbHelper dbHelper)
        {
            _dbHelper = dbHelper;
        }

        public async Task<DataTable> GetProperties()
        {
            string query = @"SELECT 
                u.UnitId,
                b.BuildingName,
                f.FloorNumber,
                u.UnitNumber,
                u.BaseRent,
                bt.Name AS PropertyType,
                c.Name AS CityName,
                us.Name AS Status,
                u.Note
            FROM Buildings b
            INNER JOIN Floors f ON f.BuildingId = b.BuildingId
            INNER JOIN Units u ON u.FloorId = f.FloorId
            INNER JOIN BuildingType bt ON b.TypeId = bt.Id
            INNER JOIN City c ON b.CityId = c.Id
            INNER JOIN UnitStatus us ON u.StatusId = us.Id
            WHERE u.IsActive = 1
            ORDER BY b.BuildingName, f.FloorNumber, u.UnitNumber;";

            return await _dbHelper.ExecuteQueryAsync(query);
        }

        public async Task<DataTable> GetBuildings()
        {
            string query = @"SELECT 
                BuildingId,
                BuildingName
            FROM Buildings
            WHERE IsActive = 1
            ORDER BY BuildingName";

            return await _dbHelper.ExecuteQueryAsync(query);
        }

        public async Task<DataTable> GetFloorsByBuilding(int buildingId)
        {
            string query = @"SELECT 
                FloorId,
                FloorNumber
            FROM Floors
            WHERE BuildingId = @BuildingId
            AND IsActive = 1";

            var parameters = new[]
            {
                new SqlParameter("@BuildingId", buildingId)
            };

            return await _dbHelper.ExecuteQueryAsync(query, parameters);
        }

        public async Task<DataTable> SaveBuilding(string buildingName, int cityId, int typeId, string address)
        {
            string query = @"INSERT INTO Buildings 
                (BuildingName, CityId, TypeId, Address, IsActive)
                VALUES (@BuildingName, @CityId, @TypeId, @Address, 1)";

            var parameters = new[]
            {
                new SqlParameter("@BuildingName", buildingName),
                new SqlParameter("@CityId", cityId),
                new SqlParameter("@TypeId", typeId),
                new SqlParameter("@Address", address)
            };

            return await _dbHelper.ExecuteQueryAsync(query, parameters);
        }

        public async Task<DataTable> SaveFloor(int buildingId, int floorNumber)
        {
            string query = @"INSERT INTO Floors 
                (BuildingId, FloorNumber, IsActive)
                VALUES (@BuildingId, @FloorNumber, 1)";

            var parameters = new[]
            {
                new SqlParameter("@BuildingId", buildingId),
                new SqlParameter("@FloorNumber", floorNumber)
            };

            return await _dbHelper.ExecuteQueryAsync(query, parameters);
        }

        public async Task<DataTable> SaveUnit(int floorId, int buildingId, int unitNumber, int statusId, double baseRent, string note)
        {
            string query = @"INSERT INTO Units (
                FloorId,
                BuildingId,
                UnitNumber,
                StatusId,
                BaseRent,
                Note,
                IsActive
            )
            VALUES (
                @FloorId,
                @BuildingId,
                @UnitNumber,
                @StatusId,
                @BaseRent,
                @Note,
                1
            )";

            var parameters = new[]
            {
                new SqlParameter("@FloorId", floorId),
                new SqlParameter("@BuildingId", buildingId),
                new SqlParameter("@UnitNumber", unitNumber),
                new SqlParameter("@StatusId", statusId),
                new SqlParameter("@BaseRent", baseRent),
                new SqlParameter("@Note", note)
            };

            return await _dbHelper.ExecuteQueryAsync(query, parameters);
        }

        public async Task<DataTable> UpdateUnit(int unitId, int floorId, int buildingId, int unitNumber, int statusId, double baseRent, string note)
        {
            string query = @"UPDATE Units
            SET 
                FloorId = @FloorId,
                BuildingId = @BuildingId,
                UnitNumber = @UnitNumber,
                StatusId = @StatusId,
                BaseRent = @BaseRent,
                Note = @Note
            WHERE UnitId = @UnitId";

            var parameters = new[]
            {
                new SqlParameter("@UnitId", unitId),
                new SqlParameter("@FloorId", floorId),
                new SqlParameter("@BuildingId", buildingId),
                new SqlParameter("@UnitNumber", unitNumber),
                new SqlParameter("@StatusId", statusId),
                new SqlParameter("@BaseRent", baseRent),
                new SqlParameter("@Note", note)
            };

            return await _dbHelper.ExecuteQueryAsync(query, parameters);
        }

        public async Task<DataTable> DeleteUnit(int unitId)
        {
            string query = @"UPDATE Units
            SET IsActive = 0
            WHERE UnitId = @UnitId";

            var parameters = new[]
            {
                new SqlParameter("@UnitId", unitId)
            };

            return await _dbHelper.ExecuteQueryAsync(query, parameters);
        }
    }
}