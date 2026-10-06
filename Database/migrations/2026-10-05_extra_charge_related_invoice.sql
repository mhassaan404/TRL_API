-- Extra charges: an extra-charge invoice (RentInvoices row with ChargeType set) can point to the invoice it relates
-- to, e.g. a Rent Correction for an invoice that was billed too low. Optional; the original invoice is never changed.
-- Only adds a nullable column, a foreign key to RentInvoices(Id) and an index; no existing data is changed.
-- Safe to re-run.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
IF COL_LENGTH('dbo.RentInvoices', 'RelatedInvoiceId') IS NULL
    ALTER TABLE dbo.RentInvoices ADD RelatedInvoiceId INT NULL;
GO
IF OBJECT_ID('dbo.FK_RentInvoices_RelatedInvoice', 'F') IS NULL
    ALTER TABLE dbo.RentInvoices WITH CHECK ADD CONSTRAINT FK_RentInvoices_RelatedInvoice
        FOREIGN KEY (RelatedInvoiceId) REFERENCES dbo.RentInvoices (Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RentInvoices_RelatedInvoice' AND object_id = OBJECT_ID('dbo.RentInvoices'))
    CREATE NONCLUSTERED INDEX IX_RentInvoices_RelatedInvoice ON dbo.RentInvoices (RelatedInvoiceId) WHERE RelatedInvoiceId IS NOT NULL;
GO
