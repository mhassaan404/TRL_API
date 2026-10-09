-- Company profile: the client's own details printed on receipts, invoices, statements and reports
-- (phone, email, NTN, office address, website, footer note, logo). The company name stays in the catalog (Clients).
-- Adds dbo.CompanyProfile with its single row (Id = 1, all details empty). No existing data is changed. Safe to re-run.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
IF OBJECT_ID('dbo.CompanyProfile', 'U') IS NULL
    CREATE TABLE dbo.CompanyProfile (
        Id              INT            NOT NULL CONSTRAINT PK_CompanyProfile PRIMARY KEY,
        Phone           NVARCHAR(30)   NULL,
        Email           NVARCHAR(100)  NULL,
        Ntn             NVARCHAR(30)   NULL,
        Address         NVARCHAR(300)  NULL,
        Website         NVARCHAR(150)  NULL,
        FooterNote      NVARCHAR(200)  NULL,
        Logo            VARBINARY(MAX) NULL,
        LogoContentType VARCHAR(20)    NULL,
        UpdatedBy       INT            NULL,
        UpdatedAt       DATETIME       NULL,
        CONSTRAINT CK_CompanyProfile_SingleRow CHECK (Id = 1),
        CONSTRAINT CK_CompanyProfile_Logo CHECK (
            (Logo IS NULL AND LogoContentType IS NULL)
            OR (Logo IS NOT NULL AND DATALENGTH(Logo) <= 204800 AND LogoContentType IS NOT NULL
                AND LogoContentType IN ('image/png', 'image/jpeg')))
    );
GO
IF NOT EXISTS (SELECT 1 FROM dbo.CompanyProfile WHERE Id = 1)
    INSERT INTO dbo.CompanyProfile (Id) VALUES (1);
GO
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE Name = N'2026-10-09_company_profile')
    INSERT INTO dbo.SchemaMigrations (Name) VALUES (N'2026-10-09_company_profile');
GO
