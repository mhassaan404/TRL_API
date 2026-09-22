using System.Data;
using TRL_API.DAL;
using TRL_API.Data;
using TRL_API.Models;

namespace TRL_API.BLL
{
    public class RentHistoryService
    {
        private readonly DbHelper _dbHelper;
        private readonly RentHistoryRepository _dal;

        public RentHistoryService(RentHistoryRepository dal, DbHelper dbHelper)
        {
            _dal = dal;
            _dbHelper = dbHelper;
        }

        public async Task<DataTable> GetHistoryAsync()
        {
            return await _dal.GetHistoryAsync();
        }

        public async Task<ApiResponse> CancelInvoice(int invoiceid, string? reason, int userId)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Reason is required to cancel an invoice." };
            try
            {
                var dt = await _dal.CancelInvoice(invoiceid, reason.Trim(), userId);
                return dt.Rows[0]["Result"].ToString() switch
                {
                    "OK" => new ApiResponse { IsSuccess = true, Message = "Invoice cancelled successfully." },
                    "HAS_PAYMENTS" => new ApiResponse { IsSuccess = false, ErrorMessage = "This invoice has payment records, so it can't be cancelled." },
                    _ => new ApiResponse { IsSuccess = false, ErrorMessage = "Invoice not found or already cancelled." },
                };
            }
            catch (Exception ex)
            {
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Error occurred while cancelling invoice. " + ex.Message };
            }
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
                        ErrorMessage = "Invoice could not be reinstated."
                    };
            }
            catch (Exception ex)
            {
                return new ApiResponse
                {
                    IsSuccess = false,
                    ErrorMessage = "Error occurred while reinstating invoice. " + ex.Message
                };
            }
        }
    }
}
