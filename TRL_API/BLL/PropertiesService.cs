using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.DAL;
using TRL_API.Models;

namespace TRL_API.BLL
{
    public class PropertiesService : IPropertiesService
    {
        private readonly IPropertiesRepository _dal;

        public PropertiesService(IPropertiesRepository dal)
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
            if (string.IsNullOrWhiteSpace(buildingName))
                return new ApiResponse { IsSuccess = false, Message = "Building name is required." };
            try
            {
                var result = await _dal.SaveBuilding(buildingName, cityId, typeId, address);
                return result.IsSuccess
                    ? new ApiResponse { IsSuccess = true, Id = result.Id, Message = "Building saved successfully." }
                    : new ApiResponse { IsSuccess = false, Message = "Failed to save building." };
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                return new ApiResponse { IsSuccess = false, Message = $"A building named \"{buildingName.Trim()}\" already exists." };
            }
        }

        public async Task<ApiResponse> UpdateBuilding(int buildingId, string? buildingName, int cityId, int typeId, string? address)
        {
            if (string.IsNullOrWhiteSpace(buildingName))
                return new ApiResponse { IsSuccess = false, Message = "Building name is required." };
            try
            {
                var result = await _dal.UpdateBuilding(buildingId, buildingName, cityId, typeId, address);
                return result.IsSuccess
                    ? new ApiResponse { IsSuccess = true, Message = "Building updated successfully." }
                    : new ApiResponse { IsSuccess = false, Message = "Building not found or update failed." };
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                return new ApiResponse { IsSuccess = false, Message = $"A building named \"{buildingName.Trim()}\" already exists." };
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
        }

        // ---------------- FLOOR ----------------

        // Floor and unit numbers are text ("G", "1", "B1", "A-101"): required, at most 50 characters
        private static string? LabelError(string value, string label) =>
            string.IsNullOrWhiteSpace(value) ? $"{label} is required."
            : value.Trim().Length > 50 ? $"{label} can be at most 50 characters."
            : null;

        public async Task<ApiResponse> SaveFloor(int buildingId, string floorNumber)
        {
            if (LabelError(floorNumber, "Floor number") is string err)
                return new ApiResponse { IsSuccess = false, Message = err };
            try
            {
                var result = await _dal.SaveFloor(buildingId, floorNumber.Trim());
                return result.IsSuccess
                    ? new ApiResponse { IsSuccess = true, Id = result.Id, Message = "Floor saved successfully." }
                    : new ApiResponse { IsSuccess = false, Message = "Failed to save floor." };
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                return new ApiResponse { IsSuccess = false, Message = $"This building already has floor \"{floorNumber.Trim()}\"." };
            }
            catch (SqlException ex) when (ex.Number == 547)
            {
                return new ApiResponse { IsSuccess = false, Message = "The selected building does not exist." };
            }
        }

        public async Task<ApiResponse> UpdateFloor(int floorId, string floorNumber)
        {
            if (LabelError(floorNumber, "Floor number") is string err)
                return new ApiResponse { IsSuccess = false, Message = err };
            try
            {
                var result = await _dal.UpdateFloor(floorId, floorNumber.Trim());
                return result.IsSuccess
                    ? new ApiResponse { IsSuccess = true, Message = "Floor updated successfully." }
                    : new ApiResponse { IsSuccess = false, Message = "Floor not found or update failed." };
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                return new ApiResponse { IsSuccess = false, Message = $"This building already has floor \"{floorNumber.Trim()}\"." };
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
        }

        // ---------------- UNIT ----------------

        public async Task<ApiResponse> SaveUnit(int floorId, int buildingId, string unitNumber,
            int statusId, double baseRent, string? note, string? propertyType)
        {
            if (LabelError(unitNumber, "Unit number") is string err)
                return new ApiResponse { IsSuccess = false, Message = err };
            if (baseRent < 0)
                return new ApiResponse { IsSuccess = false, Message = "Base rent can't be negative." };
            unitNumber = unitNumber.Trim();
            try
            {
                var result = await _dal.SaveUnit(floorId, buildingId, unitNumber, statusId, baseRent, note, propertyType);
                return result.IsSuccess
                    ? new ApiResponse { IsSuccess = true, Id = result.Id, Message = "Unit saved successfully." }
                    : new ApiResponse { IsSuccess = false, Message = "Failed to save unit." };
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                return new ApiResponse { IsSuccess = false, Message = $"This floor already has unit \"{unitNumber}\"." };
            }
            catch (SqlException ex) when (ex.Number == 547)
            {
                return new ApiResponse { IsSuccess = false, Message = "Cannot save unit because the related building or floor does not exist." };
            }
        }

        public async Task<ApiResponse> UpdateUnit(int unitId, int floorId, int buildingId,
            string unitNumber, int statusId, double baseRent, string? note, string? propertyType)
        {
            if (LabelError(unitNumber, "Unit number") is string err)
                return new ApiResponse { IsSuccess = false, Message = err };
            if (baseRent < 0)
                return new ApiResponse { IsSuccess = false, Message = "Base rent can't be negative." };
            unitNumber = unitNumber.Trim();
            try
            {
                var result = await _dal.UpdateUnit(unitId, floorId, buildingId, unitNumber, statusId, baseRent, note, propertyType);
                return result.IsSuccess
                    ? new ApiResponse { IsSuccess = true, Message = "Unit updated successfully." }
                    : new ApiResponse { IsSuccess = false, Message = "Unit not found or update failed." };
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                return new ApiResponse { IsSuccess = false, Message = $"This floor already has unit \"{unitNumber}\"." };
            }
            catch (SqlException ex) when (ex.Number == 547)
            {
                return new ApiResponse { IsSuccess = false, Message = "Cannot update unit because the related building or floor does not exist." };
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
        }
    }
}