using NuGet.Protocol.Core.Types;
using System.Data;
using TRL_API.DAL;
using TRL_API.Models;

namespace TRL_API.BLL
{
    public class TenantService
    {
        private readonly TenantRepository _dal;
        public TenantService(TenantRepository dal)
        {
            _dal = dal;
        }

        public async Task<DataTable> GetTenants()
        {
            return await _dal.GetTenants();
        }

        public async Task<DataTable> GetBuildings()
        {
            return await _dal.GetBuildings();
        }

        public async Task<DataTable> GetFloors(int? buildingId)
        {
            return await _dal.GetFloors(buildingId);
        }

        public async Task<DataTable> GetUnits(int? floorId)
        {
            return await _dal.GetUnits(floorId);
        }

        public async Task<DataTable> GetCities()
        {
            return await _dal.GetCities();
        }

        public async Task<ApiResponse> SaveTenantAsync(Tenants tenant)
        {
            var (result, error) = await _dal.SaveTenantAsync(tenant);
            if (result > 0)
                return new ApiResponse { Success = true, Message = "Tenant saved successfully." };
            else
                return new ApiResponse { Success = false, ErrorMessage = "No record saved." };
        }

        public async Task<ApiResponse> UpdateTenantAsync(Tenants tenant)
        {
            var (result, error) = await _dal.UpdateTenantAsync(tenant);
            if (result > 0)
                return new ApiResponse { Success = true, Message = "Tenant updated successfully." };
            else
                return new ApiResponse { Success = false, ErrorMessage = "No record saved." };
        }

        public async Task<ApiResponse> DeleteTenantAsync(int tenantId)
        {
            var (result, error) = await _dal.DeleteTenantAsync(tenantId);

            if (!string.IsNullOrEmpty(error))
            {
                if (error.Contains("FK__RentInvoi__Tenan__"))
                    error = "Cannot delete tenant because there are existing rent invoices. Please delete invoices first.";
                else if (error.Contains("REFERENCE constraint"))
                    error = "Cannot delete tenant because it is referenced in another record.";
                else
                    error = "Error occurred while deleting tenant. Please try again.";

                return new ApiResponse { Success = false, ErrorMessage = error };
            }

            if (result > 0)
                return new ApiResponse { Success = true, Message = "Tenant deleted successfully" };

            return new ApiResponse { Success = false, ErrorMessage = "Tenant not found or already deleted." };
        }
    }
}
