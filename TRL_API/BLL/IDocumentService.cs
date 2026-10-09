using System.Data;

namespace TRL_API.BLL
{
    public interface IDocumentService
    {
        (List<int>? Ids, string? Error) ParseReceiptIds(string? ids);
        Task<(DataTable? Receipts, string? Error)> GetReceiptsAsync(List<int> paymentIds);
        Task<(DataTable Invoice, DataTable Payments)?> GetInvoiceAsync(int invoiceId);
    }
}
