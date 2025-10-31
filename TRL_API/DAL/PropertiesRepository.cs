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
                b.BuildingId,
                b.BuildingName,
                f.FloorNumber,
                u.UnitNumber,
                u.BaseRent,
                bt.Name AS BuildingType,
                b.Address,
                c.Name AS CityName,
                us.Name AS UnitStatus,
                u.Note,
                u.IsActive
            FROM Buildings b
            INNER JOIN Floors f ON f.BuildingId = b.BuildingId
            INNER JOIN Units u ON u.FloorId = f.FloorId
            INNER JOIN BuildingType bt ON b.TypeId = bt.Id
            INNER JOIN City c ON b.CityId = c.Id
            INNER JOIN UnitStatus us ON u.StatusId = us.Id;";
            var dt = await _dbHelper.ExecuteQueryAsync(query);
            return dt;
        }
    }
}
