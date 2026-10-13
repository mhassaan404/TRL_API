-- POST-DEPLOYMENT step for audit item C4: removes the old copy of a tenant's unit and rent from Tenants (BuildingId,
-- FloorId, UnitId, MonthlyRent), with its foreign keys and index. Since migration 2026-10-13_cleanup the API takes a
-- tenant's unit and rent from the leases (dbo.TenantCurrentLease) and no longer reads or writes these columns.
--
-- Kept OUT of migrations/ on purpose: earlier API versions read these columns, so run it only once an API built with
-- 2026-10-13_cleanup is live and checked. Later, move this file into migrations/ (with SchemaVersion.Required set to
-- it in that release, and 01/04 regenerated) so new databases are created without the columns.
--
-- Nothing that isn't in the leases is lost: the script stops first if an active tenant is still marked on a unit
-- without an active lease. Undo: rollback/2026-10-13b_drop_tenant_unit_copy_ROLLBACK.sql re-creates the columns and
-- fills them from the leases. ONE batch, one transaction, recorded inside it, safe to re-run.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET LOCK_TIMEOUT 30000;

IF OBJECT_ID('dbo.SchemaMigrations', 'U') IS NULL
   OR NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE Name = N'2026-10-13_cleanup')
    THROW 50003, 'This database is missing migration 2026-10-13_cleanup. Nothing was changed.', 1;
-- (dynamic SQL: on a re-run the column is gone, and a direct reference would not even compile)
DECLARE @Legacy INT = 0;
IF COL_LENGTH('dbo.Tenants', 'UnitId') IS NOT NULL
    EXEC sp_executesql N'SELECT @n = COUNT(*) FROM dbo.Tenants t WHERE t.UnitId IS NOT NULL AND t.IsActive = 1 AND t.IsDeleted = 0
                           AND NOT EXISTS (SELECT 1 FROM dbo.TenantLeases l WHERE l.UnitId = t.UnitId AND l.IsActive = 1)',
                       N'@n INT OUTPUT', @n = @Legacy OUTPUT;
IF @Legacy > 0
    THROW 50010, 'Nothing was changed: an active tenant is marked on a unit that has no active lease (give them a lease first).', 1;

BEGIN TRAN;

-- Foreign keys, indexes, defaults and user statistics that use the four columns, whatever their names
DECLARE @Sql NVARCHAR(MAX) = N'';
SELECT @Sql = CONCAT(@Sql, N'ALTER TABLE dbo.Tenants DROP CONSTRAINT ', QUOTENAME(k.name), N' ')
FROM sys.foreign_keys k
WHERE k.parent_object_id = OBJECT_ID('dbo.Tenants')
  AND EXISTS (SELECT 1 FROM sys.foreign_key_columns c WHERE c.constraint_object_id = k.object_id
              AND COL_NAME(c.parent_object_id, c.parent_column_id) IN ('BuildingId', 'FloorId', 'UnitId', 'MonthlyRent'));
SELECT @Sql = CONCAT(@Sql, N'DROP INDEX ', QUOTENAME(i.name), N' ON dbo.Tenants ')
FROM sys.indexes i
WHERE i.object_id = OBJECT_ID('dbo.Tenants') AND i.is_primary_key = 0 AND i.index_id > 0
  AND EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
              AND COL_NAME(ic.object_id, ic.column_id) IN ('BuildingId', 'FloorId', 'UnitId', 'MonthlyRent'));
SELECT @Sql = CONCAT(@Sql, N'ALTER TABLE dbo.Tenants DROP CONSTRAINT ', QUOTENAME(d.name), N' ')
FROM sys.default_constraints d
WHERE d.parent_object_id = OBJECT_ID('dbo.Tenants')
  AND COL_NAME(d.parent_object_id, d.parent_column_id) IN ('BuildingId', 'FloorId', 'UnitId', 'MonthlyRent');
SELECT @Sql = CONCAT(@Sql, N'DROP STATISTICS dbo.Tenants.', QUOTENAME(s.name), N' ')
FROM sys.stats s
WHERE s.object_id = OBJECT_ID('dbo.Tenants') AND s.user_created = 1
  AND EXISTS (SELECT 1 FROM sys.stats_columns sc WHERE sc.object_id = s.object_id AND sc.stats_id = s.stats_id
              AND COL_NAME(sc.object_id, sc.column_id) IN ('BuildingId', 'FloorId', 'UnitId', 'MonthlyRent'));
IF @Sql <> N'' EXEC (@Sql);

DECLARE @Drop NVARCHAR(400) = N'';
SELECT @Drop = CONCAT(@Drop, CASE WHEN @Drop = N'' THEN N'' ELSE N', ' END, QUOTENAME(name))
FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Tenants') AND name IN ('BuildingId', 'FloorId', 'UnitId', 'MonthlyRent');
IF @Drop <> N'' EXEC (N'ALTER TABLE dbo.Tenants DROP COLUMN ' + @Drop);

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE Name = N'2026-10-13b_drop_tenant_unit_copy')
    INSERT INTO dbo.SchemaMigrations (Name) VALUES (N'2026-10-13b_drop_tenant_unit_copy');
COMMIT;
