-- EMERGENCY ONLY: undoes 2026-10-21_user_permissions (removes dbo.UserPermissions, Users.FullName and its record).
-- Use together with redeploying an API whose SchemaVersion.Required is 2026-10-20_invoice_audit_old_amount.
-- Staff users' permissions and names are lost (the users themselves stay, but can't log in to the older API).
-- Safe to re-run. One batch, no GO.
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;

IF OBJECT_ID('dbo.UserPermissions', 'U') IS NOT NULL
    DROP TABLE dbo.UserPermissions;

IF COL_LENGTH('dbo.Users', 'FullName') IS NOT NULL
    ALTER TABLE dbo.Users DROP COLUMN FullName;

DELETE FROM dbo.SchemaMigrations WHERE Name = N'2026-10-21_user_permissions';

COMMIT;
