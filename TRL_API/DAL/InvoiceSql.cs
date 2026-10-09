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

        // Why payment row "p" (on invoice "ri") can't be reversed, or NULL when it can. Used by the reversal itself
        // (re-checked under the invoice lock) and by the invoice History so the button matches the rule.
        //   IS_REVERSAL / ALREADY_REVERSED  a reversal row, or a row reversed before
        //   SETTLEMENT   paid from the deposit / credit moved at move-out, or any record on an invoice of a finalized
        //                move-out settlement (incl. the final payment taken there)
        //   ADJUSTMENT   a negative adjustment (corrected with a new adjustment, not reversed)
        //   NOTHING      no cash, discount or waiver on the row
        //   CANCELLED    the invoice is cancelled
        //   NEGATIVE     an adjustment already took back part of it: reversing would leave negative paid/discount
        public const string ReversalBlock = @"
                CASE WHEN p.ReversalOfPaymentId IS NOT NULL THEN 'IS_REVERSAL'
                     WHEN EXISTS (SELECT 1 FROM Payments rv WHERE rv.ReversalOfPaymentId = p.Id) THEN 'ALREADY_REVERSED'
                     WHEN ISNULL(p.PaymentMethod, N'') IN (N'Security Deposit', N'Credit to Deposit') THEN 'SETTLEMENT'
                     WHEN EXISTS (SELECT 1 FROM MoveOutSettlementLines msl
                                  WHERE msl.InvoiceId = p.RentInvoiceId OR msl.CashPaymentId = p.Id) THEN 'SETTLEMENT'
                     WHEN p.PaymentAmount < 0 OR p.DiscountAmount < 0 THEN 'ADJUSTMENT'
                     WHEN p.PaymentAmount = 0 AND p.DiscountAmount = 0 AND p.IsLateFeeWaived = 0 THEN 'NOTHING'
                     WHEN ri.StatusId = 6 THEN 'CANCELLED'
                     WHEN (SELECT SUM(o.PaymentAmount) FROM Payments o WHERE o.RentInvoiceId = p.RentInvoiceId) - p.PaymentAmount < 0
                       OR (SELECT SUM(o.DiscountAmount) FROM Payments o WHERE o.RentInvoiceId = p.RentInvoiceId) - p.DiscountAmount < 0
                          THEN 'NEGATIVE'
                     ELSE NULL END";

        // Invoice "ri" has a payment record that still counts: not a reversal and not reversed
        // (an invoice whose every record was reversed can be cancelled).
        public const string HasActivePaymentRecords = @"
                EXISTS (SELECT 1 FROM Payments ap
                        WHERE ap.RentInvoiceId = ri.Id AND ap.ReversalOfPaymentId IS NULL
                          AND NOT EXISTS (SELECT 1 FROM Payments ar WHERE ar.ReversalOfPaymentId = ap.Id))";
    }
}
