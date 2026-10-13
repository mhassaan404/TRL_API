-- Phase 6 cleanup (audit items C4, C5, C6). Keeps every row of business data.
--   C4  dbo.TenantCurrentLease(@TenantId): the lease that stands for a tenant's unit and rent. The API reads this
--       (the most recently made active lease; for a moved-out tenant, their last lease that wasn't a cancelled renewal)
--       instead of the old copy in
--       Tenants.UnitId / FloorId / BuildingId / MonthlyRent (which only lease actions kept
--       up to date) and no longer writes that copy; the columns are removed later by pending/2026-10-13b, once this
--       API is live. Stops first if any answer of the API would change: an active tenant still marked on a unit with
--       no active lease (data from before leases), or an invoice without a unit whose tenant is marked on another unit
--       than the lease function gives.
--   C5  drops unused objects: RentPayments (only when empty), InvoiceStatus (an unused copy of StatusList with other
--       ids) and vw_UnitOccupancy (it read the old copy; its endpoint is removed from the API).
--   C6  fixed constraint names instead of the ones SQL Server generated (they differ per database, so no script can
--       refer to them): PK_<Table>, UQ_<Table>_<Column>, DF_<Table>_<Column>, FK_<Table>_<Column>. Users.Email becomes
--       optional (the application never uses it; it stays unique when filled in). Tenants.CreatedBy / UpdatedBy become
--       INT user ids: the API already writes user ids into these text columns; a username found there is mapped to
--       its user id.
-- ONE batch, one transaction, recorded inside it. Safe to re-run. Undo: rollback/2026-10-13_cleanup_ROLLBACK.sql.
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
   OR NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE Name = N'2026-10-12c_db_constraints')
    THROW 50003, 'This database is missing earlier migrations (2026-10-12c_db_constraints). Nothing was changed.', 1;

-- Generated constraint names to replace: system-named, or an explicit copy of a generated name (PK__ / DF__ / ...)
DECLARE @Rename TABLE (OldName SYSNAME, NewName SYSNAME, Kind NVARCHAR(60));
INSERT @Rename (OldName, NewName, Kind)
SELECT k.name, CASE k.type WHEN 'PK' THEN CONCAT('PK_', OBJECT_NAME(k.parent_object_id))
                           ELSE CONCAT('UQ_', OBJECT_NAME(k.parent_object_id), '_',
                                STUFF((SELECT '_' + COL_NAME(ic.object_id, ic.column_id) FROM sys.index_columns ic
                                       WHERE ic.object_id = k.parent_object_id AND ic.index_id = k.unique_index_id
                                       ORDER BY ic.key_ordinal FOR XML PATH('')), 1, 1, '')) END, k.type_desc
FROM sys.key_constraints k
WHERE (k.is_system_named = 1 OR k.name LIKE 'PK[_][_]%' OR k.name LIKE 'UQ[_][_]%')
  AND OBJECT_NAME(k.parent_object_id) NOT IN ('RentPayments', 'InvoiceStatus')
  AND NOT (k.type = 'UQ' AND OBJECT_NAME(k.parent_object_id) = 'Users'
           AND EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = k.parent_object_id AND ic.index_id = k.unique_index_id
                       AND COL_NAME(ic.object_id, ic.column_id) = 'Email'))
UNION ALL
SELECT d.name, CONCAT('DF_', OBJECT_NAME(d.parent_object_id), '_', COL_NAME(d.parent_object_id, d.parent_column_id)), 'DEFAULT'
FROM sys.default_constraints d
WHERE (d.is_system_named = 1 OR d.name LIKE 'DF[_][_]%') AND OBJECT_NAME(d.parent_object_id) NOT IN ('RentPayments', 'InvoiceStatus')
UNION ALL
SELECT f.name, CONCAT('FK_', OBJECT_NAME(f.parent_object_id), '_',
                      (SELECT TOP (1) COL_NAME(c.parent_object_id, c.parent_column_id) FROM sys.foreign_key_columns c
                       WHERE c.constraint_object_id = f.object_id ORDER BY c.constraint_column_id)), 'FK'
FROM sys.foreign_keys f
WHERE (f.is_system_named = 1 OR f.name LIKE 'FK[_][_]%') AND OBJECT_NAME(f.parent_object_id) NOT IN ('RentPayments', 'InvoiceStatus')
UNION ALL
SELECT c.name, CONCAT('CK_', OBJECT_NAME(c.parent_object_id), '_', ISNULL(COL_NAME(c.parent_object_id, c.parent_column_id), 'Row')), 'CHECK'
FROM sys.check_constraints c
WHERE (c.is_system_named = 1 OR c.name LIKE 'CK[_][_]%') AND OBJECT_NAME(c.parent_object_id) NOT IN ('RentPayments', 'InvoiceStatus');

DECLARE @Bad NVARCHAR(2048) = N'';
SELECT @Bad = CONCAT(@Bad, N' ', v.Msg, N': ', v.N, N'.')
FROM (VALUES
    (N'RentPayments rows (it is dropped only when empty)',
        (SELECT CASE WHEN OBJECT_ID('dbo.RentPayments', 'U') IS NULL THEN 0 ELSE (SELECT SUM(p.rows) FROM sys.partitions p
                WHERE p.object_id = OBJECT_ID('dbo.RentPayments') AND p.index_id IN (0, 1)) END)),
    (N'Tenants.CreatedBy/UpdatedBy values that are neither a user id nor a username',
        (SELECT COUNT(*) FROM dbo.Tenants t CROSS APPLY (VALUES (t.CreatedBy), (t.UpdatedBy)) a (V)
         WHERE a.V IS NOT NULL AND TYPE_NAME((SELECT system_type_id FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Tenants') AND name = 'CreatedBy')) <> 'int'
           AND TRY_CONVERT(INT, CONVERT(NVARCHAR(150), a.V)) IS NULL
           AND NOT EXISTS (SELECT 1 FROM dbo.Users u WHERE u.Username = CONVERT(NVARCHAR(150), a.V)))),
    (N'active tenants marked on a unit that has no active lease (they would no longer show the unit as occupied)',
        (SELECT COUNT(*) FROM dbo.Tenants t WHERE t.UnitId IS NOT NULL AND t.IsActive = 1 AND t.IsDeleted = 0
           AND NOT EXISTS (SELECT 1 FROM dbo.TenantLeases l WHERE l.UnitId = t.UnitId AND l.IsActive = 1))),
    (N'invoices without a unit whose tenant is marked on a unit other than the one of their lease',
        (SELECT COUNT(*) FROM dbo.RentInvoices ri JOIN dbo.Tenants t ON t.TenantId = ri.TenantId
         OUTER APPLY (SELECT TOP (1) l.UnitId FROM dbo.TenantLeases l WHERE l.TenantId = t.TenantId
                      AND (l.IsActive = 1 OR (t.IsActive = 0 AND ISNULL(l.TerminationReason, '') <> 'Renewal cancelled'))
                      ORDER BY l.IsActive DESC, l.LeaseId DESC) cl
         WHERE ri.UnitId IS NULL AND t.UnitId IS NOT NULL AND (cl.UnitId IS NULL OR cl.UnitId <> t.UnitId))),
    (N'new constraint names already used by another object',
        (SELECT COUNT(*) FROM @Rename r WHERE r.OldName <> r.NewName AND OBJECT_ID(N'dbo.' + r.NewName) IS NOT NULL)),
    (N'new constraint names that would be given twice',
        (SELECT COUNT(*) FROM (SELECT NewName FROM @Rename GROUP BY NewName HAVING COUNT(*) > 1) d))
) v (Msg, N)
WHERE v.N > 0;
IF @Bad <> N''
BEGIN
    SET @Bad = CONCAT(N'Nothing was changed. Fix these first:', @Bad);
    THROW 50010, @Bad, 1;
END

BEGIN TRAN;

-- C4: the tenant's current lease
EXEC (N'CREATE OR ALTER FUNCTION dbo.TenantCurrentLease (@TenantId INT)
RETURNS TABLE
AS
RETURN
    SELECT TOP (1) l.LeaseId, l.UnitId, l.RentAmount
    FROM dbo.TenantLeases l
    INNER JOIN dbo.Tenants t ON t.TenantId = l.TenantId
    WHERE l.TenantId = @TenantId AND (l.IsActive = 1 OR (t.IsActive = 0 AND ISNULL(l.TerminationReason, '''') <> ''Renewal cancelled''))
    ORDER BY l.IsActive DESC, l.LeaseId DESC');

-- C5: unused objects
IF OBJECT_ID('dbo.vw_UnitOccupancy', 'V') IS NOT NULL EXEC (N'DROP VIEW dbo.vw_UnitOccupancy');
IF OBJECT_ID('dbo.RentPayments', 'U') IS NOT NULL EXEC (N'DROP TABLE dbo.RentPayments');
IF OBJECT_ID('dbo.InvoiceStatus', 'U') IS NOT NULL EXEC (N'DROP TABLE dbo.InvoiceStatus');

-- C6: fixed constraint names
DECLARE @Sql NVARCHAR(MAX) = N'';
SELECT @Sql = CONCAT(@Sql, N'EXEC sp_rename N', QUOTENAME(N'dbo.' + QUOTENAME(OldName), ''''), N', N', QUOTENAME(NewName, ''''), N', N''OBJECT'' ')
FROM @Rename WHERE OldName <> NewName;
IF @Sql <> N'' EXEC (@Sql);

-- C6: Users.Email optional, still unique when filled in
DECLARE @EmailUq SYSNAME = (SELECT TOP (1) k.name FROM sys.key_constraints k
                            JOIN sys.index_columns ic ON ic.object_id = k.parent_object_id AND ic.index_id = k.unique_index_id
                            WHERE k.parent_object_id = OBJECT_ID('dbo.Users') AND k.type = 'UQ' AND COL_NAME(ic.object_id, ic.column_id) = 'Email');
IF @EmailUq IS NOT NULL EXEC (N'ALTER TABLE dbo.Users DROP CONSTRAINT ' + @EmailUq);
IF COLUMNPROPERTY(OBJECT_ID('dbo.Users'), 'Email', 'AllowsNull') = 0
BEGIN
    DECLARE @AlterEmail NVARCHAR(400);
    SELECT @AlterEmail = CONCAT(N'ALTER TABLE dbo.Users ALTER COLUMN Email NVARCHAR(', max_length / 2, N') COLLATE ', collation_name, N' NULL')
    FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'Email';
    EXEC (@AlterEmail);
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'UX_Users_Email')
    EXEC (N'CREATE UNIQUE NONCLUSTERED INDEX UX_Users_Email ON dbo.Users (Email) WHERE Email IS NOT NULL');

-- C6: Tenants.CreatedBy / UpdatedBy as INT user ids (usernames written by older versions become their user id)
IF TYPE_NAME((SELECT system_type_id FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Tenants') AND name = 'CreatedBy')) <> 'int'
BEGIN
    EXEC (N'UPDATE t SET CreatedBy = CONVERT(VARCHAR(150), u.UserId) FROM dbo.Tenants t JOIN dbo.Users u ON u.Username = t.CreatedBy
            WHERE TRY_CONVERT(INT, t.CreatedBy) IS NULL');
    EXEC (N'UPDATE t SET UpdatedBy = CONVERT(VARCHAR(150), u.UserId) FROM dbo.Tenants t JOIN dbo.Users u ON u.Username = t.UpdatedBy
            WHERE TRY_CONVERT(INT, t.UpdatedBy) IS NULL');
    EXEC (N'ALTER TABLE dbo.Tenants ALTER COLUMN CreatedBy INT NULL');
    EXEC (N'ALTER TABLE dbo.Tenants ALTER COLUMN UpdatedBy INT NULL');
END

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE Name = N'2026-10-13_cleanup')
    INSERT INTO dbo.SchemaMigrations (Name) VALUES (N'2026-10-13_cleanup');
COMMIT;
