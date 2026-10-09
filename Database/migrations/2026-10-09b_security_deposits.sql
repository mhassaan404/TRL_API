-- Security deposits: money held for a tenancy (tenant + unit), NOT rent or income. Kept apart from RentInvoices
-- and Payments, so rent billing, balances, late fees and reports are unaffected.
--   dbo.SecurityDeposits      every deposit entry: Received (money in, > 0) or Correction (fixes a wrong entry, < 0,
--                             reason required). The held balance is the sum of a tenancy's entries.
--   dbo.SecurityDepositTerms  the agreed deposit for a tenancy (optional), so a partly paid deposit shows what is still due.
-- The balance follows the tenancy, so it carries over when a lease is renewed (the renewal is a new lease, same unit).
-- No existing data is changed. Safe to re-run.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
IF OBJECT_ID('dbo.SecurityDeposits', 'U') IS NULL
    CREATE TABLE dbo.SecurityDeposits (
        Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SecurityDeposits PRIMARY KEY,
        TenantId      INT            NOT NULL,
        UnitId        INT            NOT NULL,
        LeaseId       INT            NOT NULL,
        EntryType     NVARCHAR(20)   NOT NULL,
        Amount        DECIMAL(18, 2) NOT NULL,
        EntryDate     DATE           NOT NULL,
        PaymentMethod NVARCHAR(30)   NULL,
        Reference     NVARCHAR(100)  NULL,
        Notes         NVARCHAR(500)  NULL,
        CreatedBy     INT            NULL,
        CreatedAt     DATETIME       NOT NULL CONSTRAINT DF_SecurityDeposits_CreatedAt DEFAULT (GETDATE()),
        CONSTRAINT CK_SecurityDeposits_EntryType CHECK (EntryType IN ('Received', 'Correction')),
        CONSTRAINT CK_SecurityDeposits_Amount CHECK (
            (EntryType = 'Received' AND Amount > 0 AND Amount <= 1000000000)
            OR (EntryType = 'Correction' AND Amount < 0 AND Amount >= -1000000000)),
        CONSTRAINT CK_SecurityDeposits_Method CHECK (
            EntryType <> 'Received'
            OR (PaymentMethod IS NOT NULL AND PaymentMethod IN ('Cash', 'Bank Transfer', 'Cheque', 'Online'))),
        CONSTRAINT CK_SecurityDeposits_Reason CHECK (EntryType <> 'Correction' OR (Notes IS NOT NULL AND LEN(Notes) > 0))
    );
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SecurityDeposits_Tenancy' AND object_id = OBJECT_ID('dbo.SecurityDeposits'))
    CREATE NONCLUSTERED INDEX IX_SecurityDeposits_Tenancy ON dbo.SecurityDeposits (TenantId, UnitId);
GO
IF OBJECT_ID('dbo.FK_SecurityDeposits_Tenant', 'F') IS NULL
    ALTER TABLE dbo.SecurityDeposits ADD CONSTRAINT FK_SecurityDeposits_Tenant FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (TenantId);
IF OBJECT_ID('dbo.FK_SecurityDeposits_Unit', 'F') IS NULL
    ALTER TABLE dbo.SecurityDeposits ADD CONSTRAINT FK_SecurityDeposits_Unit FOREIGN KEY (UnitId) REFERENCES dbo.Units (UnitId);
IF OBJECT_ID('dbo.FK_SecurityDeposits_Lease', 'F') IS NULL
    ALTER TABLE dbo.SecurityDeposits ADD CONSTRAINT FK_SecurityDeposits_Lease FOREIGN KEY (LeaseId) REFERENCES dbo.TenantLeases (LeaseId);
GO
IF OBJECT_ID('dbo.SecurityDepositTerms', 'U') IS NULL
    CREATE TABLE dbo.SecurityDepositTerms (
        TenantId     INT            NOT NULL,
        UnitId       INT            NOT NULL,
        AgreedAmount DECIMAL(18, 2) NOT NULL,
        UpdatedBy    INT            NULL,
        UpdatedAt    DATETIME       NOT NULL CONSTRAINT DF_SecurityDepositTerms_UpdatedAt DEFAULT (GETDATE()),
        CONSTRAINT PK_SecurityDepositTerms PRIMARY KEY (TenantId, UnitId),
        CONSTRAINT CK_SecurityDepositTerms_Amount CHECK (AgreedAmount > 0 AND AgreedAmount <= 1000000000)
    );
GO
IF OBJECT_ID('dbo.FK_SecurityDepositTerms_Tenant', 'F') IS NULL
    ALTER TABLE dbo.SecurityDepositTerms ADD CONSTRAINT FK_SecurityDepositTerms_Tenant FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (TenantId);
IF OBJECT_ID('dbo.FK_SecurityDepositTerms_Unit', 'F') IS NULL
    ALTER TABLE dbo.SecurityDepositTerms ADD CONSTRAINT FK_SecurityDepositTerms_Unit FOREIGN KEY (UnitId) REFERENCES dbo.Units (UnitId);
GO
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE Name = N'2026-10-09b_security_deposits')
    INSERT INTO dbo.SchemaMigrations (Name) VALUES (N'2026-10-09b_security_deposits');
GO
