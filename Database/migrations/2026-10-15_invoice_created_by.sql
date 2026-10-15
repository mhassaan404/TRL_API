-- Who created each invoice: RentInvoices.CreatedBy (user id, like Payments.CreatedBy). Filled by the API from now on
-- (monthly rent, extra charges, maintenance billing, settlement deductions); older invoices stay NULL (unknown).
-- Adds one nullable column only: no data is changed. Safe to re-run. One batch, no GO.
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE Name = N'2026-10-13_cleanup')
    THROW 50000, 'This database is missing earlier migrations (2026-10-13_cleanup). Nothing was changed.', 1;

BEGIN TRAN;

IF COL_LENGTH('dbo.RentInvoices', 'CreatedBy') IS NULL
    ALTER TABLE dbo.RentInvoices ADD CreatedBy INT NULL;

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE Name = N'2026-10-15_invoice_created_by')
    INSERT INTO dbo.SchemaMigrations (Name) VALUES (N'2026-10-15_invoice_created_by');

COMMIT;
