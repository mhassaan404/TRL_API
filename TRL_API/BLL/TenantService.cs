using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.DAL;
using TRL_API.Models;

namespace TRL_API.BLL
{
    public class TenantService : ITenantService
    {
        private readonly ITenantRepository _dal;

        public TenantService(ITenantRepository dal)
        {
            _dal = dal;
        }


        public async Task<DataTable> GetTenants()
            => await _dal.GetTenants();


        public async Task<ApiResponse> SaveTenantAsync(Tenants tenant)
        {
            try
            {

                var result = await _dal.SaveTenantAsync(tenant);

                if (result.IsSuccess)
                    return new ApiResponse { IsSuccess = true, Message = "Tenant saved successfully." };

                return new ApiResponse { IsSuccess = false, Message = "No record saved." };
            }
            // ADDED: duplicate active tenant on the same unit
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                return new ApiResponse { IsSuccess = false, Message = "This unit already has an active tenant." };
            }
        }

        public async Task<ApiResponse> UpdateTenantAsync(Tenants tenant)
        {
            try
            {
                // ADDED: auto-fill rent from the unit if not provided

                // Leases drive occupancy and billing: moving a tenant out goes through Lease > Terminate
                if (tenant.IsActive != true && await _dal.HasActiveLeaseAsync(tenant.TenantId))
                    return new ApiResponse { IsSuccess = false, Message = "This tenant has an active lease. Terminate the lease to move the tenant out." };

                var result = await _dal.UpdateTenantAsync(tenant);

                if (result.IsSuccess)
                    return new ApiResponse { IsSuccess = true, Message = "Tenant updated successfully." };

                return new ApiResponse { IsSuccess = false, Message = "No record updated." };
            }
            // ADDED: duplicate active tenant on the same unit (checked before the existing FK catch)
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                return new ApiResponse { IsSuccess = false, Message = "This unit already has an active tenant." };
            }
            catch (SqlException ex) when (ex.Number == 547) // foreign key violation
            {
                return new ApiResponse
                {
                    IsSuccess = false,
                    Message = "Cannot update tenant because it is referenced in another record."
                };
            }
        }


        public async Task<ApiResponse> DeleteTenantAsync(int tenantId, int userId)
        {
            try
            {
                if (await _dal.HasActiveLeaseAsync(tenantId))
                    return new ApiResponse { IsSuccess = false, Message = "This tenant has an active lease. Terminate the lease before deleting the tenant." };

                var result = await _dal.DeleteTenantAsync(tenantId, userId);

                if (result.IsSuccess)
                    return new ApiResponse { IsSuccess = true, Message = "Tenant deleted successfully." };

                return new ApiResponse { IsSuccess = false, Message = "Tenant not found or already deleted." };
            }
            catch (SqlException ex) when (ex.Number == 547) // foreign key violation
            {
                return new ApiResponse
                {
                    IsSuccess = false,
                    Message = "Cannot delete tenant because it is referenced in another record or there are related invoices."
                };
            }
        }
    }
}
