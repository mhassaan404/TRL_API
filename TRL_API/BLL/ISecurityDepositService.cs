using System.Data;
using TRL_API.Models;

namespace TRL_API.BLL
{
    public interface ISecurityDepositService
    {
        Task<DataTable> GetTenanciesAsync();
        Task<DataTable> GetHistoryAsync(int tenantId, int unitId);
        Task<ApiResponse> RecordAsync(RecordDepositRequest r, int userId);
        Task<ApiResponse> CorrectAsync(CorrectDepositRequest r, int userId);
        Task<ApiResponse> SetAgreedAsync(SetDepositAgreedRequest r, int userId);
        Task<(DataTable? Receipt, string? Error)> GetReceiptAsync(int depositId);
    }
}
