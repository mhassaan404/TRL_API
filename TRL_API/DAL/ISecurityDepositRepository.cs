using System.Data;
using TRL_API.Models;

namespace TRL_API.DAL
{
    public interface ISecurityDepositRepository
    {
        Task<DataTable> GetTenanciesAsync();
        Task<DataTable> GetHistoryAsync(int tenantId, int unitId);
        Task<(string Result, decimal? Info)> RecordAsync(RecordDepositRequest r, int userId);
        Task<(string Result, decimal? Info)> CorrectAsync(CorrectDepositRequest r, int userId);
        Task<(string Result, decimal? Info)> SetAgreedAsync(SetDepositAgreedRequest r, int userId);
        Task<DataTable> GetReceiptAsync(int depositId);
    }
}
