-- Rent changes on an invoice keep the amount BEFORE the change too: InvoiceAudit.OldAmount (RENT_ADJUSTED rows;
-- Amount stays the new amount). The Tenant Statement can then show an invoice at the amount it was billed for on its
-- date and the change as its own line on the day it happened, so every running balance is what it really was.
-- Existing RENT_ADJUSTED rows are filled in only where the old amount is certain:
--   - a later change on the same invoice: the amount the previous change left;
--   - the first change, when it lowered the rent: before a lease is ended / cancelled / its renewal cancelled it bills
--     whole months, so the invoice was billed by the same formula as dbo.LeaseMonthCharge without an end date.
-- Anything else stays NULL and the statement shows that invoice as before. No business data is changed.
-- Safe to re-run. One batch, no GO (the new column is used through sp_executesql).
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE Name = N'2026-10-15_invoice_created_by')
    THROW 50000, 'This database is missing earlier migrations (2026-10-15_invoice_created_by). Nothing was changed.', 1;

BEGIN TRAN;

IF COL_LENGTH('dbo.InvoiceAudit', 'OldAmount') IS NULL
    ALTER TABLE dbo.InvoiceAudit ADD OldAmount DECIMAL(18, 2) NULL;

EXEC sp_executesql N'
    -- First change on a rent invoice that lowered it: the whole-month amount (no end date), same formula as
    -- dbo.LeaseMonthCharge
    UPDATE a SET OldAmount = f.Amount
    FROM dbo.InvoiceAudit a
    JOIN dbo.RentInvoices ri ON ri.Id = a.InvoiceId
    JOIN dbo.TenantLeases l ON l.LeaseId = ri.LeaseId
    CROSS APPLY (SELECT CASE WHEN l.StartDate > ri.InvoiceMonth THEN l.StartDate ELSE ri.InvoiceMonth END AS FromDate,
                        EOMONTH(ri.InvoiceMonth) AS ToDate, DAY(EOMONTH(ri.InvoiceMonth)) AS DaysInMonth) d
    CROSS APPLY (SELECT DATEDIFF(DAY, d.FromDate, d.ToDate) + 1 AS Days) x
    CROSS APPLY (SELECT CAST(CASE WHEN x.Days <= 0 THEN 0
                                  WHEN x.Days = d.DaysInMonth THEN l.RentAmount
                                  ELSE ROUND(l.RentAmount * x.Days / d.DaysInMonth, 0) END AS DECIMAL(18, 2)) AS Amount) f
    WHERE a.Action = ''RENT_ADJUSTED'' AND a.OldAmount IS NULL AND ri.ChargeType IS NULL
      AND NOT EXISTS (SELECT 1 FROM dbo.InvoiceAudit b WHERE b.InvoiceId = a.InvoiceId AND b.Action = ''RENT_ADJUSTED'' AND b.Id < a.Id)
      AND f.Amount > a.Amount;

    -- Later changes: the amount the previous change left
    UPDATE a SET OldAmount = p.Amount
    FROM dbo.InvoiceAudit a
    CROSS APPLY (SELECT TOP (1) b.Amount FROM dbo.InvoiceAudit b
                 WHERE b.InvoiceId = a.InvoiceId AND b.Action = ''RENT_ADJUSTED'' AND b.Id < a.Id ORDER BY b.Id DESC) p
    WHERE a.Action = ''RENT_ADJUSTED'' AND a.OldAmount IS NULL;';

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE Name = N'2026-10-20_invoice_audit_old_amount')
    INSERT INTO dbo.SchemaMigrations (Name) VALUES (N'2026-10-20_invoice_audit_old_amount');

COMMIT;
