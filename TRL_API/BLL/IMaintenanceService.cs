using System.Data;
using TRL_API.Models;

namespace TRL_API.BLL
{
    public interface IMaintenanceService
    {
        Task<DataTable> GetAllAsync();
        Task<DataTable> GetLogAsync(int jobId);
        Task<DataTable> GetBuildingsAsync();
        Task<DataTable> GetUnitsAsync();
        Task<ApiResponse> SaveAsync(MaintenanceJobRequest req, int userId);
        Task<ApiResponse> ChangeStatusAsync(ChangeMaintenanceStatusRequest req, int userId);
        Task<ApiResponse> BillAsync(BillMaintenanceRequest req, int userId);
    }
}
