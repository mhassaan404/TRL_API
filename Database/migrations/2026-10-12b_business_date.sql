-- One business date for the whole system: today in Pakistan (UTC+5, no daylight saving), whatever time zone the
-- database server runs in. Before this, late fees and Arrears used the UTC date while leases, rent roll and the
-- API used the server's local date, so they could disagree for up to 5 hours a day.
--   dbo.BusinessToday()   today's business date (UTC + 5 hours); C# has the same rule in Helpers/BusinessDate.cs
--   dbo.InvoiceBalance    overdue and the open late fee now use dbo.BusinessToday() instead of the UTC date
-- No data is changed. Safe to re-run.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
CREATE OR ALTER FUNCTION dbo.BusinessToday()
RETURNS DATE
AS
BEGIN
    RETURN CAST(DATEADD(HOUR, 5, SYSUTCDATETIME()) AS DATE);
END
GO
CREATE OR ALTER FUNCTION dbo.InvoiceBalance (@InvoiceId INT, @ExcludePaymentId INT)
RETURNS TABLE
AS
RETURN
    SELECT ri.Id AS InvoiceId, p.Paid, p.Disc, p.Waived, p.LastPaymentDate,
           ri.TotalRent - p.Paid - p.Disc AS RentBalance,
           ri.TotalRent + ri.LateFeeCharged - p.Paid - p.Disc AS Balance,
           CAST(CASE WHEN ri.LateFeeCharged = 0 AND p.Waived = 0
                          AND ri.DueDate < dbo.BusinessToday()
                          AND ri.TotalRent - p.Paid - p.Disc > 0
                     THEN dbo.CalculateLateFee(ri.TotalRent - p.Paid - p.Disc, ri.TotalRent, ri.DueDate, dbo.BusinessToday(),
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
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE Name = N'2026-10-12b_business_date')
    INSERT INTO dbo.SchemaMigrations (Name) VALUES (N'2026-10-12b_business_date');
GO
