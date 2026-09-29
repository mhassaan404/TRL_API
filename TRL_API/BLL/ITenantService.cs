using System.Data;
using TRL_API.DAL;
using TRL_API.Models;

namespace TRL_API.BLL
{
    public interface ITenantService
    {
        Task<DataTable> GetTenants();
        Task<ApiResponse> SaveTenantAsync(Tenants tenant);
        Task<ApiResponse> UpdateTenantAsync(Tenants tenant);
        Task<ApiResponse> DeleteTenantAsync(int tenantId, int userId);
    }
}
