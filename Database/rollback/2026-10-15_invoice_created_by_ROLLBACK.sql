-- EMERGENCY ONLY: undoes 2026-10-15_invoice_created_by (removes RentInvoices.CreatedBy and its record).
-- Who created each invoice since then is lost. Use together with redeploying an API whose SchemaVersion.Required
-- is 2026-10-13_cleanup. Safe to re-run. One batch, no GO.
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;

IF COL_LENGTH('dbo.RentInvoices', 'CreatedBy') IS NOT NULL
    ALTER TABLE dbo.RentInvoices DROP COLUMN CreatedBy;

DELETE FROM dbo.SchemaMigrations WHERE Name = N'2026-10-15_invoice_created_by';

COMMIT;
