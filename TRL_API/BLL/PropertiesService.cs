//using Microsoft.Data.SqlClient;
//using System.Data;
//using TRL_API.DAL;
//using TRL_API.Models;

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

//        public async Task<DataTable> GetFloorsByBuilding(int buildingId)
//        {
//            return await _dal.GetFloorsByBuilding(buildingId);
//        }

//        public async Task<DataTable> GetUnitsByFloor(int floorId)
//        {
//            return await _dal.GetUnitsByFloor(floorId);
//        }

//        public async Task<DataTable> GetUnitsStatus()
//        {
//            return await _dal.GetUnitsStatus();
//        }

//        public async Task<ApiResponse> SaveBuilding(string? buildingName, int cityId, int typeId, string? address)
//        {
//            try
//            {
//                var result = await _dal.SaveBuilding(buildingName, cityId, typeId, address);

//                if (result.IsSuccess)
//                {
//                    return new ApiResponse
//                    {
//                        IsSuccess = true,
//                        Message = "Building saved successfully."
//                    };
//                }

//                return new ApiResponse
//                {
//                    IsSuccess = false,
//                    Message = "Failed to save building."
//                };
//            }
//            catch (Exception ex)
//            {
//                return new ApiResponse
//                {
//                    IsSuccess = false,
//                    Message = "Error occurred while saving building. " + ex.Message
//                };
//            }
//        }
//        public async Task<ApiResponse> SaveFloor(int buildingId, int floorNumber)
//        {
//            try
//            {
//                var result = await _dal.SaveFloor(buildingId, floorNumber);

//                if (result.IsSuccess)
//                {
//                    return new ApiResponse
//                    {
//                        IsSuccess = true,
//                        Message = "Floor saved successfully."
//                    };
//                }

//                return new ApiResponse
//                {
//                    IsSuccess = false,
//                    Message = "Failed to save floor."
//                };
//            }
//            catch (Exception ex)
//            {
//                return new ApiResponse
//                {
//                    IsSuccess = false,
//                    Message = "Error occurred while saving floor. " + ex.Message
//                };
//            }
//        }

//        public async Task<ApiResponse> SaveUnit(int floorId, int buildingId, int unitNumber,
//            int statusId, double baseRent, string? note)
//        {
//            try
//            {
//                var result = await _dal.SaveUnit(floorId, buildingId, unitNumber,
//                    statusId, baseRent, note);

//                if (result.IsSuccess)
//                {
//                    return new ApiResponse
//                    {
//                        IsSuccess = true,
//                        Message = "Unit saved successfully."
//                    };
//                }

//                return new ApiResponse
//                {
//                    IsSuccess = false,
//                    Message = "Failed to save unit."
//                };
//            }
//            catch (SqlException ex) when (ex.Number == 547)
//            {
//                return new ApiResponse
//                {
//                    IsSuccess = false,
//                    Message = "Cannot save unit because the related building or floor does not exist."
//                };
//            }
//            catch (Exception ex)
//            {
//                return new ApiResponse
//                {
//                    IsSuccess = false,
//                    Message = "Error occurred while saving unit. " + ex.Message
//                };
//            }
//        }

//        public async Task<ApiResponse> UpdateUnit(int unitId, int floorId, int buildingId,
//            int unitNumber, int statusId, double baseRent, string? note)
//        {
//            try
//            {
//                var result = await _dal.UpdateUnit(unitId, floorId, buildingId,
//                    unitNumber, statusId, baseRent, note);

//                if (result.IsSuccess)
//                {
//                    return new ApiResponse
//                    {
//                        IsSuccess = true,
//                        Message = "Unit updated successfully."
//                    };
//                }

//                return new ApiResponse
//                {
//                    IsSuccess = false,
//                    Message = "Unit not found or update failed."
//                };
//            }
//            catch (SqlException ex) when (ex.Number == 547)
//            {
//                return new ApiResponse
//                {
//                    IsSuccess = false,
//                    Message = "Cannot update unit because the related building or floor does not exist."
//                };
//            }
//            catch (Exception ex)
//            {
//                return new ApiResponse
//                {
//                    IsSuccess = false,
//                    Message = "Error occurred while updating unit. " + ex.Message
//                };
//            }
//        }

//        public async Task<ApiResponse> DeleteUnitAsync(int unitId)
//        {
//            try
//            {
//                var result = await _dal.DeleteUnit(unitId);

//                if (result.IsSuccess)
//                {
//                    return new ApiResponse
//                    {
//                        IsSuccess = true,
//                        Message = "Unit deleted successfully."
//                    };
//                }

//                return new ApiResponse
//                {
//                    IsSuccess = false,
//                    Message = "Unit not found or already deleted."
//                };
//            }
//            catch (SqlException ex) when (ex.Number == 547)
//            {
//                return new ApiResponse
//                {
//                    IsSuccess = false,
//                    Message = "Cannot delete unit because it is referenced in another record."
//                };
//            }
//            catch (Exception ex)
//            {
//                return new ApiResponse
//                {
//                    IsSuccess = false,
//                    Message = "Error occurred while deleting unit. " + ex.Message
//                };
//            }
//        }
//    }
//}




using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.DAL;
using TRL_API.Models;

namespace TRL_API.BLL
{
    public class PropertiesService
    {
        private readonly PropertiesRepository _dal;

        public PropertiesService(PropertiesRepository dal)
        {
            _dal = dal;
        }

        public async Task<DataTable> GetProperties() => await _dal.GetProperties();
        public async Task<DataTable> GetBuildings() => await _dal.GetBuildings();
        public async Task<DataTable> GetFloorsByBuilding(int buildingId) => await _dal.GetFloorsByBuilding(buildingId);
        public async Task<DataTable> GetUnitsByFloor(int floorId) => await _dal.GetUnitsByFloor(floorId);
        public async Task<DataTable> GetUnitsStatus() => await _dal.GetUnitsStatus();
        public async Task<DataTable> GetCities() => await _dal.GetCities();
        public async Task<DataTable> GetBuildingTypes() => await _dal.GetBuildingTypes();

        // ---------------- BUILDING ----------------

        public async Task<ApiResponse> SaveBuilding(string? buildingName, int cityId, int typeId, string? address)
        {
            try
            {
                var result = await _dal.SaveBuilding(buildingName, cityId, typeId, address);
                return result.IsSuccess
                    ? new ApiResponse { IsSuccess = true, Id = result.Id, Message = "Building saved successfully." }
                    : new ApiResponse { IsSuccess = false, Message = "Failed to save building." };
            }
            catch (Exception ex)
            {
                return new ApiResponse { IsSuccess = false, Message = "Error occurred while saving building. " + ex.Message };
            }
        }

        public async Task<ApiResponse> UpdateBuilding(int buildingId, string? buildingName, int cityId, int typeId, string? address)
        {
            try
            {
                var result = await _dal.UpdateBuilding(buildingId, buildingName, cityId, typeId, address);
                return result.IsSuccess
                    ? new ApiResponse { IsSuccess = true, Message = "Building updated successfully." }
                    : new ApiResponse { IsSuccess = false, Message = "Building not found or update failed." };
            }
            catch (Exception ex)
            {
                return new ApiResponse { IsSuccess = false, Message = "Error occurred while updating building. " + ex.Message };
            }
        }

        public async Task<ApiResponse> DeleteBuilding(int buildingId)
        {
            try
            {
                var result = await _dal.DeleteBuilding(buildingId);
                return result.IsSuccess
                    ? new ApiResponse { IsSuccess = true, Message = "Building deleted successfully." }
                    : new ApiResponse { IsSuccess = false, Message = "Building not found or already deleted." };
            }
            catch (SqlException ex) when (ex.Number == 547)
            {
                return new ApiResponse { IsSuccess = false, Message = "Cannot delete building because it has floors/units referencing it." };
            }
            catch (Exception ex)
            {
                return new ApiResponse { IsSuccess = false, Message = "Error occurred while deleting building. " + ex.Message };
            }
        }

        // ---------------- FLOOR ----------------

        public async Task<ApiResponse> SaveFloor(int buildingId, int floorNumber)
        {
            try
            {
                var result = await _dal.SaveFloor(buildingId, floorNumber);
                return result.IsSuccess
                    ? new ApiResponse { IsSuccess = true, Id = result.Id, Message = "Floor saved successfully." }
                    : new ApiResponse { IsSuccess = false, Message = "Failed to save floor." };
            }
            catch (Exception ex)
            {
                return new ApiResponse { IsSuccess = false, Message = "Error occurred while saving floor. " + ex.Message };
            }
        }

        public async Task<ApiResponse> UpdateFloor(int floorId, int floorNumber)
        {
            try
            {
                var result = await _dal.UpdateFloor(floorId, floorNumber);
                return result.IsSuccess
                    ? new ApiResponse { IsSuccess = true, Message = "Floor updated successfully." }
                    : new ApiResponse { IsSuccess = false, Message = "Floor not found or update failed." };
            }
            catch (Exception ex)
            {
                return new ApiResponse { IsSuccess = false, Message = "Error occurred while updating floor. " + ex.Message };
            }
        }

        public async Task<ApiResponse> DeleteFloor(int floorId)
        {
            try
            {
                var result = await _dal.DeleteFloor(floorId);
                return result.IsSuccess
                    ? new ApiResponse { IsSuccess = true, Message = "Floor deleted successfully." }
                    : new ApiResponse { IsSuccess = false, Message = "Floor not found or already deleted." };
            }
            catch (SqlException ex) when (ex.Number == 547)
            {
                return new ApiResponse { IsSuccess = false, Message = "Cannot delete floor because it has units referencing it." };
            }
            catch (Exception ex)
            {
                return new ApiResponse { IsSuccess = false, Message = "Error occurred while deleting floor. " + ex.Message };
            }
        }

        // ---------------- UNIT ----------------

        public async Task<ApiResponse> SaveUnit(int floorId, int buildingId, int unitNumber,
            int statusId, double baseRent, string? note, string? propertyType)
        {
            try
            {
                var result = await _dal.SaveUnit(floorId, buildingId, unitNumber, statusId, baseRent, note, propertyType);
                return result.IsSuccess
                    ? new ApiResponse { IsSuccess = true, Id = result.Id, Message = "Unit saved successfully." }
                    : new ApiResponse { IsSuccess = false, Message = "Failed to save unit." };
            }
            catch (SqlException ex) when (ex.Number == 547)
            {
                return new ApiResponse { IsSuccess = false, Message = "Cannot save unit because the related building or floor does not exist." };
            }
            catch (Exception ex)
            {
                return new ApiResponse { IsSuccess = false, Message = "Error occurred while saving unit. " + ex.Message };
            }
        }

        public async Task<ApiResponse> UpdateUnit(int unitId, int floorId, int buildingId,
            int unitNumber, int statusId, double baseRent, string? note, string? propertyType)
        {
            try
            {
                var result = await _dal.UpdateUnit(unitId, floorId, buildingId, unitNumber, statusId, baseRent, note, propertyType);
                return result.IsSuccess
                    ? new ApiResponse { IsSuccess = true, Message = "Unit updated successfully." }
                    : new ApiResponse { IsSuccess = false, Message = "Unit not found or update failed." };
            }
            catch (SqlException ex) when (ex.Number == 547)
            {
                return new ApiResponse { IsSuccess = false, Message = "Cannot update unit because the related building or floor does not exist." };
            }
            catch (Exception ex)
            {
                return new ApiResponse { IsSuccess = false, Message = "Error occurred while updating unit. " + ex.Message };
            }
        }

        public async Task<ApiResponse> DeleteUnitAsync(int unitId)
        {
            try
            {
                var result = await _dal.DeleteUnit(unitId);
                return result.IsSuccess
                    ? new ApiResponse { IsSuccess = true, Message = "Unit deleted successfully." }
                    : new ApiResponse { IsSuccess = false, Message = "Unit not found or already deleted." };
            }
            catch (SqlException ex) when (ex.Number == 547)
            {
                return new ApiResponse { IsSuccess = false, Message = "Cannot delete unit because it is referenced in another record." };
            }
            catch (Exception ex)
            {
                return new ApiResponse { IsSuccess = false, Message = "Error occurred while deleting unit. " + ex.Message };
            }
        }
    }
}