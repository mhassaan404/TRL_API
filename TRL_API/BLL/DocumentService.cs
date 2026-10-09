using System.Data;
using TRL_API.DAL;

namespace TRL_API.BLL
{
    // Printable documents. Receipt number = RCT- + payment Id, invoice number = INV- + invoice Id (formatted on the
    // page), so every receipt/invoice already has its own unique number and nothing new is stored.
    public class DocumentService : IDocumentService
    {
        // Receipts printed together (e.g. one payment that paid several invoices)
        public const int MaxReceipts = 20;

        private readonly IDocumentRepository _dal;
        public DocumentService(IDocumentRepository dal) => _dal = dal;

        // "12,13" -> [12, 13]. Ids must be positive whole numbers; duplicates are dropped.
        public (List<int>? Ids, string? Error) ParseReceiptIds(string? ids)
        {
            if (string.IsNullOrWhiteSpace(ids)) return (null, "Please choose a payment.");
            var parts = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var list = new List<int>();
            foreach (var part in parts)
            {
                if (!int.TryParse(part, out var id) || id <= 0) return (null, "Invalid payment number.");
                if (!list.Contains(id)) list.Add(id);
            }
            if (list.Count == 0) return (null, "Please choose a payment.");
            if (list.Count > MaxReceipts) return (null, $"At most {MaxReceipts} receipts can be printed at once.");
            return (list, null);
        }

        // A receipt is proof of money received, so only payments with a cash amount have one
        // (discount-only, waiver-only and adjustment/reversal records don't).
        public async Task<(DataTable? Receipts, string? Error)> GetReceiptsAsync(List<int> paymentIds)
        {
            var dt = await _dal.GetReceiptsAsync(paymentIds);
            foreach (var id in paymentIds)
            {
                var row = dt.AsEnumerable().FirstOrDefault(r => Convert.ToInt32(r["PaymentId"]) == id);
                if (row == null) return (null, $"Payment #{id} was not found.");
                if (Convert.ToDecimal(row["PaymentAmount"]) <= 0)
                    return (null, $"Payment #{id} has no receipt: no money was received (discount, waiver or adjustment only).");
                // Paid from the security deposit / credit moved at a move-out settlement: no new money was received
                var method = row["PaymentMethod"] as string;
                if (method == SettlementRepository.DepositMethod || method == SettlementRepository.CreditMethod)
                    return (null, $"Payment #{id} has no receipt: it was paid from the security deposit at move-out, not received as new money.");
            }
            return (dt, null);
        }

        // null = invoice not found
        public async Task<(DataTable Invoice, DataTable Payments)?> GetInvoiceAsync(int invoiceId)
        {
            var invoice = await _dal.GetInvoiceAsync(invoiceId);
            if (invoice.Rows.Count == 0) return null;
            return (invoice, await _dal.GetInvoicePaymentsAsync(invoiceId));
        }
    }
}
