using System.Data;
using TRL_API.Data;
using TRL_API.Models;

namespace TRL_API.DAL
{
    public interface ITenantRepository
    {
        Task<DataTable> GetTenants();
        Task<ApiResponse> SaveTenantAsync(Tenants tenant);
        Task<ApiResponse> UpdateTenantAsync(Tenants tenant);
        Task<ApiResponse> DeleteTenantAsync(int tenantId, int userId);
        Task<bool> HasActiveLeaseAsync(int tenantId);
    }
}
