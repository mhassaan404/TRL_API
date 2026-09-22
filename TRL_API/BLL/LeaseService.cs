using System.Data;
using TRL_API.DAL;
using TRL_API.Models;

namespace TRL_API.BLL
{
    public class LeaseService
    {
        private readonly LeaseRepository _dal;
        public LeaseService(LeaseRepository dal) => _dal = dal;

        public async Task<DataTable> GetAllAsync() => await _dal.GetAllAsync();
        public async Task<DataTable> GetByTenantAsync(int tenantId) => await _dal.GetByTenantAsync(tenantId);

        public async Task<ApiResponse> CreateAsync(Lease lease, int userId)
        {
            if (lease.TenantId <= 0 || lease.UnitId <= 0)
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Tenant and unit are required." };
            if (lease.RentAmount <= 0)
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Rent must be greater than zero." };
            if (lease.TenureMonths <= 0)
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Tenure must be at least 1 month." };

            try
            {
                var result = await _dal.CreateAsync(lease, userId);
                return result.IsSuccess
                    ? new ApiResponse { IsSuccess = true, Message = "Lease created successfully." }
                    : new ApiResponse { IsSuccess = false, ErrorMessage = "Failed to create lease." };
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627)
            {
                return new ApiResponse { IsSuccess = false, ErrorMessage = "This unit already has an active lease." };
            }
        }

        public async Task<ApiResponse> RenewAsync(RenewLeaseRequest req, int userId)
        {
            if (req.TenureMonths <= 0)
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Tenure must be at least 1 month." };
            return await _dal.RenewAsync(req, userId);
        }

        public async Task<ApiResponse> TerminateAsync(TerminateLeaseRequest req) => await _dal.TerminateAsync(req);
    }
}
