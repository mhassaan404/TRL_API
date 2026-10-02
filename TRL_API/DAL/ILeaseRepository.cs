using System.Data;
using TRL_API.Data;
using TRL_API.Models;

namespace TRL_API.DAL
{
    public interface ILeaseRepository
    {
        Task<DataTable> GetAllAsync();
        Task<DataTable> GetByTenantAsync(int tenantId);
        Task<DateTime?> GetOverlappingBilledThroughAsync(int unitId, DateTime startDate);
        Task<ApiResponse> CreateAsync(Lease lease, int userId);
        Task<ApiResponse> RenewAsync(RenewLeaseRequest req, int userId);
        Task<ApiResponse> TerminateAsync(TerminateLeaseRequest req, DateTime moveOutDate, int userId);
        Task<ApiResponse> CancelRenewalAsync(int leaseId, int userId);
        Task<ApiResponse> CancelLeaseAsync(int leaseId, int userId);
    }
}
