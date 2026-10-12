using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.DAL;
using TRL_API.Helpers;
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
            var invalid = TenantValidation.Normalize(tenant);
            if (invalid != null)
                return new ApiResponse { IsSuccess = false, Message = invalid };

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
            var invalid = TenantValidation.Normalize(tenant);
            if (invalid != null)
                return new ApiResponse { IsSuccess = false, Message = invalid };

            try
            {

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
            static ApiResponse Fail(string m) => new() { IsSuccess = false, Message = m, ErrorMessage = m };
            return await _dal.DeleteTenantAsync(tenantId, userId) switch
            {
                "OK" => new ApiResponse { IsSuccess = true, Message = "Tenant deleted successfully." },
                "HAS_LEASE" => Fail("This tenant has an active lease. Terminate the lease before deleting the tenant."),
                "OPEN_BALANCE" => Fail("This tenant still owes money or has a credit on an invoice. Settle it first (Move-out Settlements)."),
                "DEPOSIT_HELD" => Fail("A security deposit is still held for this tenant. Refund or apply it first (Move-out Settlements)."),
                _ => Fail("Tenant not found or already deleted."),
            };
        }
    }
}
