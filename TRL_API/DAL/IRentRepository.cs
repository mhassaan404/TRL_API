using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.Data;
using TRL_API.Models;

namespace TRL_API.DAL
{
    public interface IRentRepository
    {
        Task<SqlConnection> GetOpenConnectionAsync();
        Task<DataTable> GetTenantsAsync();
        Task<DataTable> GetInvoicesByTenantAsync(int tenantId);
        Task<DataTable> GetPaymentHistoryByIdAsync(int invoiceId);
        Task<DataTable> GetUnpaidInvoiceByTenant(int tenantId);
        Task<string?> ValidateAdjustmentAsync(Payments p, SqlConnection conn, SqlTransaction tx);
        Task<ApiResponse> CreatePaymentAdjustmentAsync(Payments payment, int userId, SqlConnection conn, SqlTransaction transaction);
        Task<ApiResponse> CreateRentAsync(Payments payment, int userId, SqlConnection conn, SqlTransaction transaction);
        Task<int> GetLatestPaymentIdAsync(int invoiceId, int userId, SqlConnection conn, SqlTransaction transaction);
        Task<ApiResponse> RecalcInvoiceAsync(int invoiceId, SqlConnection conn, SqlTransaction transaction);
        Task<string?> ValidatePaymentAsync(Payments p, SqlConnection conn, SqlTransaction tx);
        Task<DataTable> ReversePaymentAsync(int paymentId, string reason, int userId);
        Task<DataTable> ReverseLateFeeAsync(int invoiceId, string reason, int userId);
        Task<DataTable> GetOccupancyAsync();
        Task<DataTable> GetVacantUnitsAsync(int? includeUnitId);
        Task<DataTable> GetRentCollectionAsync();
        Task<DataTable> GetTenantsWithRent();
        Task<DataTable> GetPaymentHistoryAsync(int invoiceId);
        Task<ApiResponse> BulkUpdateDueDateAsync(List<int> invoiceIds, DateTime newDueDate);
        Task<DataTable> GetActiveTenantsList();
        Task<DataTable> GetLeaseChargesForMonth(int month, int year);
        Task<DataTable> GetTenantNamesAsync();
        Task<ApiResponse> CreateInvoice(int tenantId, decimal totalRent, DateTime invoiceDate, DateTime dueDate, decimal lateFeePerDay, decimal lateFeeMaxMultiplier, string? description = null, string? chargeType = null, int? leaseId = null, int? unitId = null);
        Task<DataTable> ChargeLateFeeAsync(int invoiceId);
        Task<DataTable> CreateExtraChargesAsync(IReadOnlyList<int> tenantIds, DateTime chargeDate, DateTime dueDate, string chargeType,
            string? description, decimal amount, int? relatedInvoiceId, decimal lateFeePerDay, decimal lateFeeMaxMultiplier);
        Task<DataTable> GetAllPaymentsAsync(DateTime? from, DateTime? to);
    }
}
