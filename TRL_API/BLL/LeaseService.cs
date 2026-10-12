using System.Data;
using TRL_API.Helpers;
using TRL_API.DAL;
using TRL_API.Models;

namespace TRL_API.BLL
{
    public class LeaseService : ILeaseService
    {
        private readonly ILeaseRepository _dal;
        public LeaseService(ILeaseRepository dal) => _dal = dal;

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
            if (lease.StartDate.Date < new DateTime(2000, 1, 1) || lease.StartDate.Date > BusinessDate.Today.AddYears(2))
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Please choose a start date no more than two years from today." };

            const string leased = "This unit already has an active lease.";
            try
            {
                var row = (await _dal.CreateAsync(lease, userId)).Rows[0];
                return row["Result"].ToString() switch
                {
                    "OK" => new ApiResponse { IsSuccess = true, Message = "Lease created successfully." },
                    "UNIT_NOT_FOUND" => new ApiResponse { IsSuccess = false, ErrorMessage = "The unit doesn't exist or has been deleted." },
                    "TENANT_NOT_FOUND" => new ApiResponse { IsSuccess = false, ErrorMessage = "The tenant doesn't exist or has been deleted." },
                    "OVERLAP" => new ApiResponse
                    {
                        IsSuccess = false,
                        ErrorMessage = $"This unit is billed to an earlier lease through {Convert.ToDateTime(row["Info"]):dd MMM yyyy}. Start the new lease after that date.",
                    },
                    "UNIT_LEASED" => new ApiResponse { IsSuccess = false, ErrorMessage = leased },
                    _ => new ApiResponse { IsSuccess = false, ErrorMessage = "Failed to create lease." },
                };
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627)
            {
                return new ApiResponse { IsSuccess = false, ErrorMessage = leased }; // UX_TenantLeases_ActiveUnit
            }
        }

        public async Task<ApiResponse> RenewAsync(RenewLeaseRequest req, int userId)
        {
            if (req.TenureMonths <= 0)
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Tenure must be at least 1 month." };
            // null keeps the current rent; an explicit 0 or negative would create a lease that is never billed
            if (req.NewRentAmount is <= 0)
                return new ApiResponse { IsSuccess = false, ErrorMessage = "New rent must be greater than zero (leave it empty to keep the current rent)." };
            return await _dal.RenewAsync(req, userId);
        }

        public async Task<ApiResponse> CancelRenewalAsync(CancelRenewalRequest req, int userId)
        {
            if (req.LeaseId <= 0)
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Lease is required." };
            return await _dal.CancelRenewalAsync(req.LeaseId, userId);
        }

        public async Task<ApiResponse> TerminateAsync(TerminateLeaseRequest req, int userId)
        {
            if (string.IsNullOrWhiteSpace(req.Reason))
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Please enter a reason for ending the lease." };
            req.Reason = req.Reason.Trim();
            if (req.Reason.Length > 200)
                return new ApiResponse { IsSuccess = false, ErrorMessage = "The reason can be at most 200 characters." };

            var moveOut = req.MoveOutDate?.Date ?? BusinessDate.Today;
            if (moveOut > BusinessDate.Today)
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Move-out date can't be in the future. End the lease on the day the tenant leaves." };

            return await _dal.TerminateAsync(req, moveOut, userId);
        }

        // Same limits as creating a lease; the billing rules are checked in LeaseRepository.UpdateAsync
        public async Task<ApiResponse> UpdateAsync(UpdateLeaseRequest req)
        {
            if (req.LeaseId <= 0)
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Lease is required." };
            if (req.StartDate == null)
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Start date is required." };
            if (req.RentAmount is null or <= 0)
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Rent must be greater than zero." };
            if (req.TenureMonths is null or <= 0)
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Tenure must be at least 1 month." };
            return await _dal.UpdateAsync(req.LeaseId, req.StartDate.Value.Date, req.RentAmount.Value, req.TenureMonths.Value);
        }

        public async Task<ApiResponse> CancelLeaseAsync(CancelLeaseRequest req, int userId)
        {
            if (req.LeaseId <= 0)
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Lease is required." };
            return await _dal.CancelLeaseAsync(req.LeaseId, userId);
        }
    }
}
