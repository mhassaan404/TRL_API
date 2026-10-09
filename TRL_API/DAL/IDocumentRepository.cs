using System.Data;

namespace TRL_API.DAL
{
    public interface IDocumentRepository
    {
        Task<DataTable> GetReceiptsAsync(IReadOnlyList<int> paymentIds);
        Task<DataTable> GetInvoiceAsync(int invoiceId);
        Task<DataTable> GetInvoicePaymentsAsync(int invoiceId);
    }
}
