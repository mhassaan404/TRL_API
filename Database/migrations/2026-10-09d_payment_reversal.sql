-- Payment reversal: a payment row entered by mistake (cash, discount and/or late-fee waiver) is reversed as a whole by
-- a new, linked Payments row (ReversalOfPaymentId = the original's Id) holding the same cash and discount as negatives.
--   - The original row is never changed or deleted; it counts as "reversed" because a reversal row points to it.
--   - Sums of PaymentAmount / DiscountAmount stay correct everywhere without changes (+X and -X cancel out).
--   - A waiver is a flag, not an amount, so dbo.InvoiceBalance ignores the waiver of a reversed row.
--   - UX_Payments_ReversalOf: a row can be reversed only once (also under concurrency).
--   - CK_Payments_Reversal: a reversal row only takes amounts back (never adds money, never waives).
-- No existing data is changed. Safe to re-run.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
IF COL_LENGTH('dbo.Payments', 'ReversalOfPaymentId') IS NULL
    ALTER TABLE dbo.Payments ADD ReversalOfPaymentId INT NULL;
GO
IF OBJECT_ID('dbo.FK_Payments_ReversalOf', 'F') IS NULL
    ALTER TABLE dbo.Payments ADD CONSTRAINT FK_Payments_ReversalOf FOREIGN KEY (ReversalOfPaymentId) REFERENCES dbo.Payments (Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Payments_ReversalOf' AND object_id = OBJECT_ID('dbo.Payments'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_Payments_ReversalOf ON dbo.Payments (ReversalOfPaymentId) WHERE ReversalOfPaymentId IS NOT NULL;
GO
IF OBJECT_ID('dbo.CK_Payments_Reversal', 'C') IS NULL
    ALTER TABLE dbo.Payments ADD CONSTRAINT CK_Payments_Reversal CHECK (
        ReversalOfPaymentId IS NULL
        OR (ReversalOfPaymentId <> Id AND PaymentAmount <= 0 AND DiscountAmount <= 0 AND IsLateFeeWaived = 0));
GO
-- Same calculation as before, except: a waiver on a reversed row no longer counts, and LastPaymentDate only looks at
-- rows that are neither reversals nor reversed.
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
                     THEN dbo.CalculateLateFee(ri.TotalRent - p.Paid - p.Disc, ri.TotalRent, ri.DueDate, GETUTCDATE(),
                                               ri.LateFeePerDay, ri.LateFeeMaxMultiplier)
                     ELSE 0 END AS DECIMAL(18, 2)) AS OpenLateFee
    FROM dbo.RentInvoices ri
    OUTER APPLY (SELECT ISNULL(SUM(x.PaymentAmount), 0) AS Paid,
                        ISNULL(SUM(x.DiscountAmount), 0) AS Disc,
                        ISNULL(MAX(CASE WHEN x.IsLateFeeWaived = 1 AND x.Reversed = 0 THEN 1 ELSE 0 END), 0) AS Waived,
                        MAX(CASE WHEN x.ReversalOfPaymentId IS NULL AND x.Reversed = 0 THEN x.PaymentDate END) AS LastPaymentDate
                 FROM (SELECT pp.PaymentAmount, pp.DiscountAmount, pp.IsLateFeeWaived, pp.PaymentDate, pp.ReversalOfPaymentId,
                              CASE WHEN EXISTS (SELECT 1 FROM dbo.Payments r WHERE r.ReversalOfPaymentId = pp.Id) THEN 1 ELSE 0 END AS Reversed
                       FROM dbo.Payments pp
                       WHERE pp.RentInvoiceId = ri.Id AND pp.Id <> @ExcludePaymentId) x) p
    WHERE ri.Id = @InvoiceId;
GO
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE Name = N'2026-10-09d_payment_reversal')
    INSERT INTO dbo.SchemaMigrations (Name) VALUES (N'2026-10-09d_payment_reversal');
GO
