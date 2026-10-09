using System.Data;
using TRL_API.DAL;
using TRL_API.Helpers;
using TRL_API.Models;

namespace TRL_API.BLL
{
    public interface IRentService
    {
        Task<DataTable> GetTenantsAsync();
        Task<DataTable> GetInvoicesByTenantAsync(int tenantId);
        Task<DataTable> GetPaymentHistoryByIdAsync(int invoiceId);
        Task<DataTable> GetRentCollectionAsync();
        Task<DataTable> GetTenantsWithRentAsync();
        Task<DataTable> GetPaymentHistoryAsync(int invoiceId);
        Task<object> GetUnpaidInvoicesByTenantAsync(int tenantId);
        Task<ApiResponse> SubmitPaymentsAsync(List<Payments> payments, int userId);
        Task<DataTable> GetOccupancyAsync();
        Task<DataTable> GetVacantUnitsAsync(int? includeUnitId);
        Task<ApiResponse> ReverseLateFeeAsync(int invoiceId, string reason, int userId);
        Task<ApiResponse> CreateRentAsync(List<Payments> payments, int userId);
        Task<ApiResponse> UpdatePaymentsAsync(List<Payments> payments, int userId);
        Task<ApiResponse> CreatePaymentAdjustmentAsync(Payments payment, int userId);
        Task<ApiResponse> BulkUpdateDueDateAsync(List<int> invoiceIds, DateTime newDueDate);
        Task<DataTable> GetActiveTenantsAsync();
        Task<ApiResponse> GenerateInvoicesAsync(int month, int year, int? dueInDays, List<int>? tenantIds);
        Task<ApiResponse> CreateExtraChargeAsync(ExtraChargeRequest req);
        Task<DataTable> GetAllPaymentsAsync(DateTime? from, DateTime? to);
        Task<ApiResponse> ChargeLateFeeAsync(int invoiceId);
    }
}
