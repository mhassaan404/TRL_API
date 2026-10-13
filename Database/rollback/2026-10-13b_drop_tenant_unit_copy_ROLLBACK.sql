-- ROLLBACK of pending/2026-10-13b_drop_tenant_unit_copy.sql: re-creates Tenants.BuildingId, FloorId, UnitId and
-- MonthlyRent with their foreign keys and index, and fills them from the leases (dbo.TenantCurrentLease), which is what
-- the old copy held. Needed only before going back to an API older than 2026-10-13_cleanup. Not in migrations/, so
-- TRL_Tools never runs it. ONE batch, one transaction, safe to re-run.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET LOCK_TIMEOUT 30000;

IF OBJECT_ID('dbo.TenantCurrentLease', 'IF') IS NULL
    THROW 50003, 'dbo.TenantCurrentLease is missing (2026-10-13_cleanup not applied). Nothing was changed.', 1;

BEGIN TRAN;

IF COL_LENGTH('dbo.Tenants', 'BuildingId') IS NULL EXEC (N'ALTER TABLE dbo.Tenants ADD BuildingId INT NULL');
IF COL_LENGTH('dbo.Tenants', 'FloorId') IS NULL EXEC (N'ALTER TABLE dbo.Tenants ADD FloorId INT NULL');
IF COL_LENGTH('dbo.Tenants', 'UnitId') IS NULL EXEC (N'ALTER TABLE dbo.Tenants ADD UnitId INT NULL');
IF COL_LENGTH('dbo.Tenants', 'MonthlyRent') IS NULL EXEC (N'ALTER TABLE dbo.Tenants ADD MonthlyRent DECIMAL(18, 2) NULL');

EXEC (N'UPDATE t SET UnitId = cl.UnitId, FloorId = u.FloorId, BuildingId = f.BuildingId, MonthlyRent = cl.RentAmount
        FROM dbo.Tenants t
        OUTER APPLY dbo.TenantCurrentLease(t.TenantId) cl
        LEFT JOIN dbo.Units u ON u.UnitId = cl.UnitId
        LEFT JOIN dbo.Floors f ON f.FloorId = u.FloorId');

IF OBJECT_ID('dbo.FK_Tenants_Buildings', 'F') IS NULL
    EXEC (N'ALTER TABLE dbo.Tenants WITH CHECK ADD CONSTRAINT FK_Tenants_Buildings FOREIGN KEY (BuildingId) REFERENCES dbo.Buildings (BuildingId)');
IF OBJECT_ID('dbo.FK_Tenants_Floors', 'F') IS NULL
    EXEC (N'ALTER TABLE dbo.Tenants WITH CHECK ADD CONSTRAINT FK_Tenants_Floors FOREIGN KEY (FloorId) REFERENCES dbo.Floors (FloorId)');
IF OBJECT_ID('dbo.FK_Tenants_Units', 'F') IS NULL
    EXEC (N'ALTER TABLE dbo.Tenants WITH CHECK ADD CONSTRAINT FK_Tenants_Units FOREIGN KEY (UnitId) REFERENCES dbo.Units (UnitId)');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Tenants') AND name = 'IX_Tenants_Building_Floor_Unit')
    EXEC (N'CREATE NONCLUSTERED INDEX IX_Tenants_Building_Floor_Unit ON dbo.Tenants (BuildingId, FloorId, UnitId)');

DELETE FROM dbo.SchemaMigrations WHERE Name = N'2026-10-13b_drop_tenant_unit_copy';
COMMIT;
