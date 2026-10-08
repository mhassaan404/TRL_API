using System.Data;

namespace TRL_API.DAL
{
    public interface IReportRepository
    {
        Task<DataTable> GetArrearsAgeingAsync();
        Task<DataTable> GetCollectionsAsync(DateTime from, DateTime to);
        Task<DataTable> GetStatementTenantsAsync();
        Task<DataTable> GetStatementTenantAsync(int tenantId);
        Task<DataTable> GetStatementEntriesAsync(int tenantId, DateTime to);
        Task<DataTable> GetBillingVsCollectionAsync(DateTime fromMonth, DateTime toMonth, int? buildingId);
        Task<DataTable> GetReportBuildingsAsync();
        Task<DataTable> GetRentRollAsync();
        Task<DataTable> GetMaintenanceCostAsync(DateTime from, DateTime to);
    }
}
