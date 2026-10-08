using System.Data;
using TRL_API.Models;

namespace TRL_API.DAL
{
    public interface IMaintenanceRepository
    {
        Task<DataTable> GetAllAsync();
        Task<DataTable> GetLogAsync(int jobId);
        Task<DataTable> GetBuildingsAsync();
        Task<DataTable> GetUnitsAsync();
        Task<(string Result, int? Id)> CreateAsync(MaintenanceJobRequest req, DateTime reportedDate, int userId);
        Task<string> UpdateAsync(MaintenanceJobRequest req, DateTime reportedDate, int userId);
        Task<string> ChangeStatusAsync(int id, string status, string? note, DateTime? completedDate, int userId);
        Task<(string Result, int? InvoiceId)> BillAsync(int id, decimal amount, string description, DateTime dueDate,
            decimal lateFeePerDay, decimal lateFeeMaxMultiplier, int userId);
    }
}
