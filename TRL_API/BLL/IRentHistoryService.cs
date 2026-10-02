using System.Data;
using TRL_API.DAL;
using TRL_API.Data;
using TRL_API.Models;

namespace TRL_API.BLL
{
    public interface IRentHistoryService
    {
        Task<DataTable> GetHistoryAsync();
        Task<(DataTable Invoice, DataTable Payments, DataTable Events)> GetInvoiceDetailsAsync(int invoiceId);
        Task<ApiResponse> CancelInvoice(int invoiceid, string? reason, int userId);
        Task<ApiResponse> ReinstateInvoice(int invoiceid);
    }
}
