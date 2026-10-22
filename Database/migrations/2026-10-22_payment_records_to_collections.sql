-- The Payment Records page was merged into the Collections report (its list, receipt links and Current / Moved out
-- filter are now on Collections). Staff users who could open Payment Records ('payments:view') get Collections
-- ('collectionsReport:view') instead, and the old permission is removed. No business data is changed.
-- Safe to re-run. One batch, no GO.
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE Name = N'2026-10-21_user_permissions')
    THROW 50000, 'This database is missing earlier migrations (2026-10-21_user_permissions). Nothing was changed.', 1;

BEGIN TRAN;

INSERT INTO dbo.UserPermissions (UserId, Permission)
SELECT DISTINCT up.UserId, N'collectionsReport:view'
FROM dbo.UserPermissions up
WHERE up.Permission = N'payments:view'
  AND NOT EXISTS (SELECT 1 FROM dbo.UserPermissions c WHERE c.UserId = up.UserId AND c.Permission = N'collectionsReport:view');

DELETE FROM dbo.UserPermissions WHERE Permission = N'payments:view';

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE Name = N'2026-10-22_payment_records_to_collections')
    INSERT INTO dbo.SchemaMigrations (Name) VALUES (N'2026-10-22_payment_records_to_collections');

COMMIT;
