-- Tenant profile fields for the new tenant form:
--   TenantType     'Company' or 'Individual' (required; existing tenants default to 'Individual')
--   ContactPerson  person to contact (mainly for companies)
--   CnicNtn        CNIC (12345-1234567-1) for individuals, NTN (1234567-8) or CNIC for companies
--   Address        postal address
-- Only adds columns; no existing data is changed or removed. Name keeps the tenant / company name,
-- Contact the phone number, Email/Notes/IsActive are unchanged. Safe to re-run.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
BEGIN TRAN;

IF COL_LENGTH('dbo.Tenants', 'TenantType') IS NULL
    ALTER TABLE dbo.Tenants ADD TenantType NVARCHAR(20) NOT NULL
        CONSTRAINT DF_Tenants_TenantType DEFAULT (N'Individual') WITH VALUES;

IF COL_LENGTH('dbo.Tenants', 'ContactPerson') IS NULL
    ALTER TABLE dbo.Tenants ADD ContactPerson NVARCHAR(150) NULL;

IF COL_LENGTH('dbo.Tenants', 'CnicNtn') IS NULL
    ALTER TABLE dbo.Tenants ADD CnicNtn NVARCHAR(20) NULL;

IF COL_LENGTH('dbo.Tenants', 'Address') IS NULL
    ALTER TABLE dbo.Tenants ADD Address NVARCHAR(500) NULL;

COMMIT;
GO

-- Separate batch: the column must exist before the constraint can reference it
IF OBJECT_ID('dbo.CK_Tenants_TenantType', 'C') IS NULL
    ALTER TABLE dbo.Tenants WITH CHECK ADD CONSTRAINT CK_Tenants_TenantType
        CHECK (TenantType IN (N'Company', N'Individual'));
GO
