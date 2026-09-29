using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.Data;
using TRL_API.Models;

namespace TRL_API.DAL
{
    public interface IPropertiesRepository
    {
        Task<DataTable> GetProperties();
        Task<DataTable> GetBuildings();
        Task<DataTable> GetFloorsByBuilding(int buildingId);
        Task<DataTable> GetUnitsByFloor(int floorId);
        Task<DataTable> GetUnitsStatus();
        Task<DataTable> GetCities();
        Task<DataTable> GetBuildingTypes();
        Task<ApiResponse> SaveBuilding(string? buildingName, int cityId, int typeId, string? address);
        Task<ApiResponse> UpdateBuilding(int buildingId, string? buildingName, int cityId, int typeId, string? address);
        Task<ApiResponse> DeleteBuilding(int buildingId);
        Task<ApiResponse> SaveFloor(int buildingId, string floorNumber);
        Task<ApiResponse> UpdateFloor(int floorId, string floorNumber);
        Task<ApiResponse> DeleteFloor(int floorId);
        Task<ApiResponse> SaveUnit(int floorId, int buildingId, string unitNumber, int statusId, double baseRent, string? note, string? propertyType);
        Task<ApiResponse> UpdateUnit(int unitId, int floorId, int buildingId, string unitNumber, int statusId, double baseRent, string? note, string? propertyType);
        Task<ApiResponse> DeleteUnit(int unitId);
    }
}
