-- Staff logins with limited access. The Super Admin (Users.Role = 'Admin') keeps full access; staff users
-- (Role = 'User') can open only the pages ticked for them (view or edit) and do only the risky actions ticked for them,
-- one row per permission in dbo.UserPermissions (e.g. 'tenants:view', 'tenants:edit', 'action:reversePayment').
-- Users.FullName: the person's name, shown on the Users page. No business data is changed.
-- Safe to re-run. One batch, no GO.
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE Name = N'2026-10-20_invoice_audit_old_amount')
    THROW 50000, 'This database is missing earlier migrations (2026-10-20_invoice_audit_old_amount). Nothing was changed.', 1;

BEGIN TRAN;

IF COL_LENGTH('dbo.Users', 'FullName') IS NULL
    ALTER TABLE dbo.Users ADD FullName NVARCHAR(100) NULL;

IF OBJECT_ID('dbo.UserPermissions', 'U') IS NULL
    CREATE TABLE dbo.UserPermissions (
        UserId INT NOT NULL,
        Permission NVARCHAR(50) NOT NULL,
        CONSTRAINT PK_UserPermissions PRIMARY KEY (UserId, Permission),
        CONSTRAINT FK_UserPermissions_UserId FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId) ON DELETE CASCADE
    );

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE Name = N'2026-10-21_user_permissions')
    INSERT INTO dbo.SchemaMigrations (Name) VALUES (N'2026-10-21_user_permissions');

COMMIT;
