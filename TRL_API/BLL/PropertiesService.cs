//using System.Data;
//using TRL_API.DAL;

//namespace TRL_API.BLL
//{
//    public class PropertiesService
//    {
//        private readonly PropertiesRepository _dal;
//        public PropertiesService(PropertiesRepository dal)
//        {
//            _dal = dal;
//        }

//        public async Task<DataTable> GetProperties()
//        {
//            return await _dal.GetProperties();
//        }

//        public async Task<DataTable> GetBuildings()
//        {
//            return await _dal.GetBuildings();
//        }
//    }
//}





using System.Data;
using TRL_API.DAL;

namespace TRL_API.BLL
{
    public class PropertiesService
    {
        private readonly PropertiesRepository _dal;

        public PropertiesService(PropertiesRepository dal)
        {
            _dal = dal;
        }

        public async Task<DataTable> GetProperties()
        {
            return await _dal.GetProperties();
        }

        public async Task<DataTable> GetBuildings()
        {
            return await _dal.GetBuildings();
        }

        public async Task<DataTable> GetFloorsByBuilding(int buildingId)
        {
            return await _dal.GetFloorsByBuilding(buildingId);
        }

        public async Task<DataTable> SaveBuilding(string buildingName, int cityId, int typeId, string address)
        {
            return await _dal.SaveBuilding(buildingName, cityId, typeId, address);
        }

        public async Task<DataTable> SaveFloor(int buildingId, int floorNumber)
        {
            return await _dal.SaveFloor(buildingId, floorNumber);
        }

        public async Task<DataTable> SaveUnit(int floorId, int buildingId, int unitNumber, int statusId, double baseRent, string note)
        {
            return await _dal.SaveUnit(floorId, buildingId, unitNumber, statusId, baseRent, note);
        }

        public async Task<DataTable> UpdateUnit(int unitId, int floorId, int buildingId, int unitNumber, int statusId, double baseRent, string note)
        {
            return await _dal.UpdateUnit(unitId, floorId, buildingId, unitNumber, statusId, baseRent, note);
        }

        public async Task<DataTable> DeleteUnit(int unitId)
        {
            return await _dal.DeleteUnit(unitId);
        }
    }
}