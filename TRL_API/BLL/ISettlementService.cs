using System.Data;
using TRL_API.Models;

namespace TRL_API.BLL
{
    public interface ISettlementService
    {
        Task<DataTable> GetCandidatesAsync();
        Task<DataTable> GetSettlementsAsync();
        Task<(DataTable Header, DataTable Invoices, DataTable Unbilled)?> GetPreviewAsync(int tenantId, int unitId);
        Task<ApiResponse> FinalizeAsync(FinalizeSettlementRequest r, int userId);
        Task<ApiResponse> RecordRefundAsync(RecordSettlementRefundRequest r, int userId);
        Task<(DataTable Header, DataTable Lines, DataTable Refunds)?> GetSettlementAsync(int id);
    }
}
