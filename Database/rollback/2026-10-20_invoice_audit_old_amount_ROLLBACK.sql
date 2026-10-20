-- EMERGENCY ONLY: undoes 2026-10-20_invoice_audit_old_amount (removes InvoiceAudit.OldAmount and its record).
-- Use together with redeploying an API whose SchemaVersion.Required is 2026-10-15_invoice_created_by.
-- The old amounts recorded since then are lost. Safe to re-run. One batch, no GO.
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;

IF COL_LENGTH('dbo.InvoiceAudit', 'OldAmount') IS NOT NULL
    ALTER TABLE dbo.InvoiceAudit DROP COLUMN OldAmount;

DELETE FROM dbo.SchemaMigrations WHERE Name = N'2026-10-20_invoice_audit_old_amount';

COMMIT;
