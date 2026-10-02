using System.Data;
using TRL_API.DAL;
using TRL_API.Models;

namespace TRL_API.BLL
{
    public interface ILeaseService
    {
        Task<DataTable> GetAllAsync();
        Task<DataTable> GetByTenantAsync(int tenantId);
        Task<ApiResponse> CreateAsync(Lease lease, int userId);
        Task<ApiResponse> RenewAsync(RenewLeaseRequest req, int userId);
        Task<ApiResponse> TerminateAsync(TerminateLeaseRequest req, int userId);
        Task<ApiResponse> CancelRenewalAsync(CancelRenewalRequest req, int userId);
    }
}
