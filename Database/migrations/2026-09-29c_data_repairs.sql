-- One-time data repairs. Safe to re-run (each step only touches rows that are still wrong).
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

BEGIN TRAN;

-- 1) Invoices whose stored PendingAmount/OverPaidAmount/StatusId don't match their payments
--    (old adjustment code inserted reversals without recalculating the invoice). Same formula as RecalcInvoiceSql.
DECLARE @Fix TABLE (InvoiceId INT PRIMARY KEY, OldPending DECIMAL(18,2));
INSERT INTO @Fix
SELECT ri.Id, ri.PendingAmount
FROM RentInvoices ri
OUTER APPLY (SELECT ISNULL(SUM(PaymentAmount),0) AS Paid, ISNULL(SUM(DiscountAmount),0) AS Disc
             FROM Payments WHERE RentInvoiceId = ri.Id) p
CROSS APPLY (SELECT ri.TotalRent + ri.LateFeeCharged - p.Paid - p.Disc AS Bal) b
WHERE ri.StatusId <> 6
  AND (ri.PendingAmount  <> CASE WHEN b.Bal < 0 THEN 0 ELSE b.Bal END
    OR ri.OverPaidAmount <> CASE WHEN b.Bal < 0 THEN -b.Bal ELSE 0 END
    OR ri.StatusId <> CASE WHEN b.Bal < 0 THEN 9 WHEN b.Bal = 0 THEN 1 WHEN p.Paid + p.Disc > 0 THEN 8 ELSE 2 END);

UPDATE ri SET
    PendingAmount  = CASE WHEN b.Bal < 0 THEN 0 ELSE b.Bal END,
    OverPaidAmount = CASE WHEN b.Bal < 0 THEN -b.Bal ELSE 0 END,
    StatusId = CASE WHEN b.Bal < 0 THEN 9 WHEN b.Bal = 0 THEN 1 WHEN p.Paid + p.Disc > 0 THEN 8 ELSE 2 END
FROM RentInvoices ri
JOIN @Fix f ON f.InvoiceId = ri.Id
OUTER APPLY (SELECT ISNULL(SUM(PaymentAmount),0) AS Paid, ISNULL(SUM(DiscountAmount),0) AS Disc
             FROM Payments WHERE RentInvoiceId = ri.Id) p
CROSS APPLY (SELECT ri.TotalRent + ri.LateFeeCharged - p.Paid - p.Disc AS Bal) b;

INSERT INTO InvoiceAudit (InvoiceId, Action, Amount, Reason)
SELECT f.InvoiceId, 'BALANCE_RECALCULATED', ri.PendingAmount,
       CONCAT(N'Data repair: pending was ', f.OldPending, N', recalculated from payments')
FROM @Fix f JOIN RentInvoices ri ON ri.Id = f.InvoiceId;

-- 2) Active tenants can't have a move-out date (it hides them from the active-tenants list)
UPDATE Tenants SET MoveOutDate = NULL WHERE IsActive = 1 AND MoveOutDate IS NOT NULL;

COMMIT;
GO
