namespace TRL_API.DAL
{
    // SQL shared by several repositories, so invoice totals are calculated the same way everywhere.
    // Balances themselves come from the dbo.InvoiceBalance function (Database/migrations/2026-09-29d).
    public static class InvoiceSql
    {
        // Recalculates PendingAmount, OverPaidAmount and StatusId from the invoice's payments.
        // Append a JOIN and/or WHERE on "ri" to choose the invoices. Status: 9 Overpaid, 1 Paid, 8 Partial, 2 Unpaid.
        public const string Recalc = @"
                UPDATE ri SET
                    PendingAmount  = CASE WHEN b.Balance < 0 THEN 0 ELSE b.Balance END,
                    OverPaidAmount = CASE WHEN b.Balance < 0 THEN -b.Balance ELSE 0 END,
                    StatusId = CASE WHEN b.Balance < 0 THEN 9
                                    WHEN b.Balance = 0 THEN 1
                                    WHEN b.Paid + b.Disc > 0 THEN 8
                                    ELSE 2 END
                FROM RentInvoices ri
                CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) b";
    }
}
