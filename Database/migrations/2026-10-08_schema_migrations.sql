-- Schema version tracking (multi-client): each client database records the migrations applied to it in
-- dbo.SchemaMigrations, so the API and TRL_Tools can tell when a client database is behind.
-- Before this, migrations were run by hand and not recorded. This script therefore records the migrations that came
-- before it as already applied (the baseline), after checking that their changes are really present; if one is
-- missing it stops and changes nothing ("apply the earlier migrations first").
-- From now on every migration ends by recording its own name here (see Database/README.md).
-- No business data is changed. Safe to re-run.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
IF COL_LENGTH('dbo.RentInvoices', 'LeaseId') IS NULL OR COL_LENGTH('dbo.RentInvoices', 'InvoiceMonth') IS NULL
   OR COL_LENGTH('dbo.TenantLeases', 'BilledThrough') IS NULL OR OBJECT_ID('dbo.InvoiceBalance') IS NULL
   OR COL_LENGTH('dbo.Tenants', 'TenantType') IS NULL OR OBJECT_ID('dbo.LateFeeSettings', 'U') IS NULL
   OR COL_LENGTH('dbo.RentInvoices', 'LateFeePerDay') IS NULL OR COL_LENGTH('dbo.RentInvoices', 'RelatedInvoiceId') IS NULL
   OR OBJECT_ID('dbo.MaintenanceJobs', 'U') IS NULL OR OBJECT_ID('dbo.UQ_Building_Unit', 'UQ') IS NULL
    THROW 50003, 'This database is missing earlier migrations. Nothing was changed; apply the earlier migrations first.', 1;
GO
IF OBJECT_ID('dbo.SchemaMigrations', 'U') IS NULL
    CREATE TABLE dbo.SchemaMigrations (
        Name      NVARCHAR(200) NOT NULL CONSTRAINT PK_SchemaMigrations PRIMARY KEY, -- migration file name without .sql
        AppliedAt DATETIME2 NOT NULL CONSTRAINT DF_SchemaMigrations_AppliedAt DEFAULT (SYSUTCDATETIME())
    );
GO
INSERT INTO dbo.SchemaMigrations (Name)
SELECT v.Name FROM (VALUES
    (N'2026-09-29_invoice_lease_unit'),
    (N'2026-09-29b_lease_billing_period'),
    (N'2026-09-29c_data_repairs'),
    (N'2026-09-29d_invoice_balance_function'),
    (N'2026-09-29e_drop_duplicate_floor_constraint'),
    (N'2026-10-01_tenant_profile_fields'),
    (N'2026-10-02_late_fee_settings'),
    (N'2026-10-05_extra_charge_related_invoice'),
    (N'2026-10-07_maintenance_jobs'),
    (N'2026-10-07b_unit_number_unique_per_building'),
    (N'2026-10-08_schema_migrations')
) v(Name)
WHERE NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations m WHERE m.Name = v.Name);
GO
