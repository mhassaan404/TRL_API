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

        public async Task<(DataTable Invoice, DataTable Payments, DataTable Events, DataTable Charges)> GetInvoiceDetailsAsync(int invoiceId) =>
            await _dal.GetInvoiceDetailsAsync(invoiceId);

        public const int InvoiceAuditReasonMax = 300; // InvoiceAudit.Reason nvarchar(300)

        public async Task<ApiResponse> CancelInvoice(int invoiceid, string? reason, int userId)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Reason is required to cancel an invoice." };
            if (reason.Trim().Length > InvoiceAuditReasonMax)
                return new ApiResponse { IsSuccess = false, ErrorMessage = $"The reason can be at most {InvoiceAuditReasonMax} characters." };
            var dt = await _dal.CancelInvoice(invoiceid, reason.Trim(), userId);
            return dt.Rows[0]["Result"].ToString() switch
            {
                "OK" => new ApiResponse { IsSuccess = true, Message = "Invoice cancelled successfully." },
                "SETTLED" => new ApiResponse { IsSuccess = false, ErrorMessage = "This invoice is part of a move-out settlement, so it can't be cancelled." },
                "HAS_PAYMENTS" => new ApiResponse { IsSuccess = false, ErrorMessage = "This invoice has payment records, so it can't be cancelled. Reverse them first (Rent History → History)." },
                _ => new ApiResponse { IsSuccess = false, ErrorMessage = "Invoice not found or already cancelled." },
            };
        }

        public async Task<ApiResponse> ReinstateInvoice(int invoiceid, int userId)
        {
            static ApiResponse Fail(string m) => new() { IsSuccess = false, Message = m, ErrorMessage = m };
            try
            {
                return await _dal.ReinstateInvoice(invoiceid, userId) switch
                {
                    "OK" => new ApiResponse { IsSuccess = true, Message = "Invoice reinstated successfully." },
                    "SETTLED" => Fail("This tenancy has a move-out settlement, so its invoices can't be reinstated."),
                    "NOT_FOUND" => Fail("Invoice not found, or it is not cancelled."),
                    _ => Fail("Invoice could not be reinstated: its lease no longer covers that month."),
                };
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627)
            {
                return Fail("This month's rent for this lease has already been invoiced again, so the cancelled invoice can't be reinstated.");
            }
        }
    }
}
