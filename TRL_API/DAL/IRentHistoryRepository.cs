using System.Data;
using TRL_API.Data;
using TRL_API.Models;

namespace TRL_API.DAL
{
    public interface IRentHistoryRepository
    {
        Task<DataTable> GetHistoryAsync();
        Task<DataTable> CancelInvoice(int invoiceId, string reason, int userId);
        Task<ApiResponse> ReinstateInvoice(int invoiceId);
    }
}
