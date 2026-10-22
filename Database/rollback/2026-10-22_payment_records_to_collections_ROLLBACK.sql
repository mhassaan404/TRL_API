-- EMERGENCY ONLY: undoes the record of 2026-10-22_payment_records_to_collections, together with redeploying an API
-- whose SchemaVersion.Required is 2026-10-21_user_permissions. Staff users keep Collections; Payment Records access
-- has to be ticked again on the Users page of the older version (who had it before can't be known any more).
-- Safe to re-run. One batch, no GO.
SET NOCOUNT ON;

DELETE FROM dbo.SchemaMigrations WHERE Name = N'2026-10-22_payment_records_to_collections';
