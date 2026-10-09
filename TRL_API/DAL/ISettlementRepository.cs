using System.Data;
using TRL_API.Models;

namespace TRL_API.DAL
{
    public interface ISettlementRepository
    {
        Task<DataTable> GetCandidatesAsync();
        Task<DataTable> GetSettlementsAsync();
        Task<DataTable> GetPreviewHeaderAsync(int tenantId, int unitId);
        Task<DataTable> GetOpenInvoicesAsync(int tenantId, int unitId);
        Task<DataTable> GetUnbilledMonthsAsync(int tenantId, int unitId);
        Task<(string Result, int? Id, string? ReceiptIds)> FinalizeAsync(FinalizeSettlementRequest r, int userId);
        Task<(string Result, decimal? Info)> RecordRefundAsync(RecordSettlementRefundRequest r, int userId);
        Task<DataTable> GetSettlementAsync(int id);
        Task<DataTable> GetSettlementLinesAsync(int id);
        Task<DataTable> GetSettlementRefundsAsync(int id);
    }
}
