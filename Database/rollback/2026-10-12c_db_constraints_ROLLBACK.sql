-- ROLLBACK of migrations/2026-10-12c_db_constraints.sql. Schema only: no row is changed except the SchemaMigrations record.
-- Not in migrations/ on purpose, so TRL_Tools never runs it. Only for an emergency, and only together with going back
-- to an API build whose SchemaVersion.Required is 2026-10-12b_business_date or older (a 12c API refuses logins once
-- the record is removed).
-- Removes exactly what 12c created, by the names 12c uses:
--   indexes IX_InvoiceAudit_Invoice, UX_RefreshTokens_Token, IX_RefreshTokens_User, IX_Payments_Tenant (IX_RentInvoices_TenantId back)
--   checks  CK_RentInvoices_Amounts, CK_RentInvoices_LateFeeTerms, CK_RentInvoices_ChargeType, CK_Payments_Method, CK_TenantLeases_Terms
--   FKs     FK_RentInvoices_Tenant, FK_RentInvoices_Status, FK_InvoiceAudit_Invoice
--   column  RentInvoices.StatusId back to NULL
-- Not undone (by design): a foreign key that existed before 12c under another name, which 12c only re-checked.
-- One batch, one transaction, safe to re-run.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET LOCK_TIMEOUT 30000;

BEGIN TRAN;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.RentInvoices') AND name = 'IX_RentInvoices_TenantId')
    CREATE NONCLUSTERED INDEX IX_RentInvoices_TenantId ON dbo.RentInvoices (TenantId);
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Payments') AND name = 'IX_Payments_Tenant')
    DROP INDEX IX_Payments_Tenant ON dbo.Payments;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.RefreshTokens') AND name = 'IX_RefreshTokens_User')
    DROP INDEX IX_RefreshTokens_User ON dbo.RefreshTokens;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.RefreshTokens') AND name = 'UX_RefreshTokens_Token')
    DROP INDEX UX_RefreshTokens_Token ON dbo.RefreshTokens;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.InvoiceAudit') AND name = 'IX_InvoiceAudit_Invoice')
    DROP INDEX IX_InvoiceAudit_Invoice ON dbo.InvoiceAudit;

IF OBJECT_ID('dbo.CK_TenantLeases_Terms', 'C') IS NOT NULL ALTER TABLE dbo.TenantLeases DROP CONSTRAINT CK_TenantLeases_Terms;
IF OBJECT_ID('dbo.CK_Payments_Method', 'C') IS NOT NULL ALTER TABLE dbo.Payments DROP CONSTRAINT CK_Payments_Method;
IF OBJECT_ID('dbo.CK_RentInvoices_ChargeType', 'C') IS NOT NULL ALTER TABLE dbo.RentInvoices DROP CONSTRAINT CK_RentInvoices_ChargeType;
IF OBJECT_ID('dbo.CK_RentInvoices_LateFeeTerms', 'C') IS NOT NULL ALTER TABLE dbo.RentInvoices DROP CONSTRAINT CK_RentInvoices_LateFeeTerms;
IF OBJECT_ID('dbo.CK_RentInvoices_Amounts', 'C') IS NOT NULL ALTER TABLE dbo.RentInvoices DROP CONSTRAINT CK_RentInvoices_Amounts;

IF OBJECT_ID('dbo.FK_InvoiceAudit_Invoice', 'F') IS NOT NULL ALTER TABLE dbo.InvoiceAudit DROP CONSTRAINT FK_InvoiceAudit_Invoice;
IF OBJECT_ID('dbo.FK_RentInvoices_Status', 'F') IS NOT NULL ALTER TABLE dbo.RentInvoices DROP CONSTRAINT FK_RentInvoices_Status;
IF OBJECT_ID('dbo.FK_RentInvoices_Tenant', 'F') IS NOT NULL ALTER TABLE dbo.RentInvoices DROP CONSTRAINT FK_RentInvoices_Tenant;

-- StatusId back to NULL. A foreign key on it that 12c did not create would block the change: then stop.
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.RentInvoices') AND name = 'StatusId' AND is_nullable = 0)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_key_columns c WHERE c.parent_object_id = OBJECT_ID('dbo.RentInvoices')
               AND COL_NAME(c.parent_object_id, c.parent_column_id) = 'StatusId')
        THROW 50020, 'RentInvoices.StatusId has a foreign key that 12c did not create. Nothing was changed.', 1;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.RentInvoices') AND name = 'UX_RentInvoices_RentPerLeaseMonth')
        DROP INDEX UX_RentInvoices_RentPerLeaseMonth ON dbo.RentInvoices;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.RentInvoices') AND name = 'IX_RentInvoices_Tenant')
        DROP INDEX IX_RentInvoices_Tenant ON dbo.RentInvoices;
    ALTER TABLE dbo.RentInvoices ALTER COLUMN StatusId INT NULL;
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.RentInvoices') AND name = 'IX_RentInvoices_Tenant')
    CREATE NONCLUSTERED INDEX IX_RentInvoices_Tenant ON dbo.RentInvoices (TenantId, StatusId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.RentInvoices') AND name = 'UX_RentInvoices_RentPerLeaseMonth')
    CREATE UNIQUE NONCLUSTERED INDEX UX_RentInvoices_RentPerLeaseMonth ON dbo.RentInvoices (LeaseId, InvoiceMonth)
        WHERE ChargeType IS NULL AND StatusId <> 6 AND LeaseId IS NOT NULL;

DELETE FROM dbo.SchemaMigrations WHERE Name = N'2026-10-12c_db_constraints';
COMMIT;
