using System.Data;
using TRL_API.DAL;
using TRL_API.Data;
using TRL_API.Models;

namespace TRL_API.BLL
{
    public class RentHistoryService : IRentHistoryService
    {
        private readonly IRentHistoryRepository _dal;

        public RentHistoryService(IRentHistoryRepository dal) => _dal = dal;

        public async Task<DataTable> GetHistoryAsync()
        {
            return await _dal.GetHistoryAsync();
        }

        public async Task<ApiResponse> CancelInvoice(int invoiceid, string? reason, int userId)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Reason is required to cancel an invoice." };
            var dt = await _dal.CancelInvoice(invoiceid, reason.Trim(), userId);
            return dt.Rows[0]["Result"].ToString() switch
            {
                "OK" => new ApiResponse { IsSuccess = true, Message = "Invoice cancelled successfully." },
                "HAS_PAYMENTS" => new ApiResponse { IsSuccess = false, ErrorMessage = "This invoice has payment records, so it can't be cancelled." },
                _ => new ApiResponse { IsSuccess = false, ErrorMessage = "Invoice not found or already cancelled." },
            };
        }

        public async Task<ApiResponse> ReinstateInvoice(int invoiceid)
        {
            try
            {
                var result = await _dal.ReinstateInvoice(invoiceid);

                return result.IsSuccess
                    ? new ApiResponse
                    {
                        IsSuccess = true,
                        Message = "Invoice reinstated successfully."
                    }
                    : new ApiResponse
                    {
                        IsSuccess = false,
                        ErrorMessage = "Invoice could not be reinstated: it is not cancelled, or its lease no longer covers that month."
                    };
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627)
            {
                return new ApiResponse
                {
                    IsSuccess = false,
                    ErrorMessage = "This month's rent for this lease has already been invoiced again, so the cancelled invoice can't be reinstated."
                };
            }
        }
    }
}
