using System.Data;
using TRL_API.Models;

namespace TRL_API.BLL
{
    public interface IReportService
    {
        Task<DataTable> GetArrearsAgeingAsync();
        string? ValidateCollectionsRange(DateTime? from, DateTime? to);
        Task<DataTable> GetCollectionsAsync(DateTime from, DateTime to);
        Task<DataTable> GetStatementTenantsAsync();
        string? ValidateStatementRange(DateTime? from, DateTime? to);
        Task<TenantStatement?> GetTenantStatementAsync(int tenantId, DateTime from, DateTime to);
        string? ValidateMonthRange(DateTime? fromMonth, DateTime? toMonth);
        Task<DataTable> GetBillingVsCollectionAsync(DateTime fromMonth, DateTime toMonth, int? buildingId);
        Task<DataTable> GetReportBuildingsAsync();
        Task<DataTable> GetRentRollAsync();
        string? ValidateMaintenanceRange(DateTime? from, DateTime? to);
        Task<DataTable> GetMaintenanceCostAsync(DateTime from, DateTime to);
    }
}
