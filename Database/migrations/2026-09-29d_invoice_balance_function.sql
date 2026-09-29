-- One shared definition of an invoice's totals, used by every query that needs balances/late fees
-- (rent collection, unpaid invoices, history, dashboard, payment validation, late fees, status recalculation).
--   @ExcludePaymentId: leave one payment out (validating an edit to that payment); 0 = include all.
-- Balance       = rent + charged late fee - payments - discounts   (negative = credit/overpaid)
-- RentBalance   = rent - payments - discounts                      (rent still owed, late fee excluded)
-- OpenLateFee   = late fee that applies now but isn't charged yet  (overdue, rent still owed, not waived)
-- Safe to re-run.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

CREATE OR ALTER FUNCTION dbo.InvoiceBalance (@InvoiceId INT, @ExcludePaymentId INT)
RETURNS TABLE
AS
RETURN
    SELECT ri.Id AS InvoiceId, p.Paid, p.Disc, p.Waived, p.LastPaymentDate,
           ri.TotalRent - p.Paid - p.Disc AS RentBalance,
           ri.TotalRent + ri.LateFeeCharged - p.Paid - p.Disc AS Balance,
           CAST(CASE WHEN ri.LateFeeCharged = 0 AND p.Waived = 0
                          AND ri.DueDate < CAST(GETUTCDATE() AS DATE)
                          AND ri.TotalRent - p.Paid - p.Disc > 0
                     THEN dbo.CalculateLateFee(ri.TotalRent - p.Paid - p.Disc, ri.TotalRent, ri.DueDate, GETUTCDATE())
                     ELSE 0 END AS DECIMAL(18, 2)) AS OpenLateFee
    FROM dbo.RentInvoices ri
    OUTER APPLY (SELECT ISNULL(SUM(PaymentAmount), 0) AS Paid,
                        ISNULL(SUM(DiscountAmount), 0) AS Disc,
                        ISNULL(MAX(CASE WHEN IsLateFeeWaived = 1 THEN 1 ELSE 0 END), 0) AS Waived,
                        MAX(PaymentDate) AS LastPaymentDate
                 FROM dbo.Payments
                 WHERE RentInvoiceId = ri.Id AND Id <> @ExcludePaymentId) p
    WHERE ri.Id = @InvoiceId;
GO
