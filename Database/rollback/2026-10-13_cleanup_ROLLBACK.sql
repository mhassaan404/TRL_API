-- ROLLBACK of migrations/2026-10-13_cleanup.sql. Not in migrations/ on purpose, so TRL_Tools never runs it. Only for an
-- emergency, and only together with going back to an API build whose SchemaVersion.Required is 2026-10-12c_db_constraints
-- (a 2026-10-13 API refuses logins once the record is removed). No business data is changed.
-- Puts back: vw_UnitOccupancy, RentPayments (empty, as it was required to be) and InvoiceStatus (with its 5 rows),
-- Users.Email required (stops if a user has no email), Tenants.CreatedBy / UpdatedBy as text; refills the Tenants unit
-- and rent copy from the leases (the 2026-10-13 API stopped maintaining it) and drops TenantCurrentLease.
-- Not undone: the new constraint names (the old ones were random per database and have no meaning). Stops if
-- pending/2026-10-13b already removed the Tenants unit columns (roll that back first).
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

IF COL_LENGTH('dbo.Tenants', 'UnitId') IS NULL
    THROW 50021, 'Tenants.UnitId is gone (pending/2026-10-13b was applied): roll that back first. Nothing was changed.', 1;
IF EXISTS (SELECT 1 FROM dbo.Users WHERE Email IS NULL)
    THROW 50022, 'Some users have no email, so Users.Email cannot be made required again. Nothing was changed.', 1;

BEGIN TRAN;

IF TYPE_NAME((SELECT system_type_id FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Tenants') AND name = 'CreatedBy')) = 'int'
BEGIN
    EXEC (N'ALTER TABLE dbo.Tenants ALTER COLUMN CreatedBy VARCHAR(150) NULL');
    EXEC (N'ALTER TABLE dbo.Tenants ALTER COLUMN UpdatedBy VARCHAR(150) NULL');
END

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'UX_Users_Email')
    EXEC (N'DROP INDEX UX_Users_Email ON dbo.Users');
IF COLUMNPROPERTY(OBJECT_ID('dbo.Users'), 'Email', 'AllowsNull') = 1
BEGIN
    DECLARE @AlterEmail NVARCHAR(400);
    SELECT @AlterEmail = CONCAT(N'ALTER TABLE dbo.Users ALTER COLUMN Email NVARCHAR(', max_length / 2, N') COLLATE ', collation_name, N' NOT NULL')
    FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'Email';
    EXEC (@AlterEmail);
END
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints k JOIN sys.index_columns ic ON ic.object_id = k.parent_object_id AND ic.index_id = k.unique_index_id
               WHERE k.parent_object_id = OBJECT_ID('dbo.Users') AND k.type = 'UQ' AND COL_NAME(ic.object_id, ic.column_id) = 'Email')
    EXEC (N'ALTER TABLE dbo.Users ADD CONSTRAINT UQ_Users_Email UNIQUE NONCLUSTERED (Email)');

IF OBJECT_ID('dbo.InvoiceStatus', 'U') IS NULL
BEGIN
    EXEC (N'CREATE TABLE dbo.InvoiceStatus (Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InvoiceStatus PRIMARY KEY CLUSTERED,
            Name NVARCHAR(50) NOT NULL, IsActive BIT NOT NULL CONSTRAINT DF_InvoiceStatus_IsActive DEFAULT ((1)))');
    EXEC (N'SET IDENTITY_INSERT dbo.InvoiceStatus ON
            INSERT INTO dbo.InvoiceStatus (Id, Name, IsActive) VALUES (1, N''Pending'', 1), (2, N''Unpaid'', 1), (3, N''Partial'', 1), (4, N''Paid'', 1), (5, N''Overpaid'', 1)
            SET IDENTITY_INSERT dbo.InvoiceStatus OFF');
END

IF OBJECT_ID('dbo.RentPayments', 'U') IS NULL
    EXEC (N'CREATE TABLE dbo.RentPayments (
            Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RentPayments PRIMARY KEY CLUSTERED,
            RentInvoiceId INT NOT NULL CONSTRAINT FK_RentPayments_RentInvoiceId REFERENCES dbo.RentInvoices (Id),
            TenantId INT NOT NULL,
            PaymentAmount DECIMAL(18, 2) NOT NULL CONSTRAINT DF_RentPayments_PaymentAmount DEFAULT ((0)),
            DiscountAmount DECIMAL(18, 2) NOT NULL CONSTRAINT DF_RentPayments_DiscountAmount DEFAULT ((0)),
            IsLateFeeWaived BIT NOT NULL CONSTRAINT DF_RentPayments_IsLateFeeWaived DEFAULT ((0)),
            PaymentMethod NVARCHAR(50) NULL,
            PaymentDate DATE NULL,
            Notes NVARCHAR(500) NULL,
            CreatedAt DATETIME NOT NULL CONSTRAINT DF_RentPayments_CreatedAt DEFAULT (getdate()))');

IF OBJECT_ID('dbo.vw_UnitOccupancy', 'V') IS NULL
    EXEC (N'CREATE VIEW vw_UnitOccupancy AS
SELECT b.BuildingId, b.BuildingName, f.FloorId, f.FloorNumber, u.UnitId, u.UnitNumber,
       u.BaseRent AS UnitRent,
       tl.LeaseId, t.TenantId, t.Name AS TenantName, tl.RentAmount AS LeaseRent, t.MonthlyRent AS TenantRent,
       tl.StartDate, tl.EndDate, tl.TenureMonths,
       CASE WHEN t.TenantId IS NULL THEN ''Vacant'' ELSE ''Occupied'' END AS Occupancy
FROM Units u
JOIN Floors f ON f.FloorId = u.FloorId
JOIN Buildings b ON b.BuildingId = f.BuildingId
OUTER APPLY (SELECT TOP 1 TenantId, Name, MonthlyRent FROM Tenants
             WHERE UnitId = u.UnitId AND IsActive = 1 ORDER BY TenantId) t
LEFT JOIN TenantLeases tl ON tl.TenantId = t.TenantId AND tl.UnitId = u.UnitId AND tl.IsActive = 1');

-- The 2026-10-13 API no longer keeps Tenants.UnitId / FloorId / BuildingId / MonthlyRent up to date: refill that copy
-- from the leases, so the previous API (which reads it) shows the right unit and rent
IF OBJECT_ID('dbo.TenantCurrentLease', 'IF') IS NOT NULL
BEGIN
    EXEC (N'UPDATE t SET UnitId = cl.UnitId, FloorId = u.FloorId, BuildingId = f.BuildingId, MonthlyRent = cl.RentAmount
            FROM dbo.Tenants t
            OUTER APPLY dbo.TenantCurrentLease(t.TenantId) cl
            LEFT JOIN dbo.Units u ON u.UnitId = cl.UnitId
            LEFT JOIN dbo.Floors f ON f.FloorId = u.FloorId');
    EXEC (N'DROP FUNCTION dbo.TenantCurrentLease');
END

DELETE FROM dbo.SchemaMigrations WHERE Name = N'2026-10-13_cleanup';
COMMIT;
