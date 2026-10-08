-- Usernames must be unique within a client database: login finds the user by Client Code + Username.
-- Adds UQ_Users_Username. If two users already share a username the script stops and changes nothing.
-- No data is changed. Safe to re-run.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
IF EXISTS (SELECT 1 FROM dbo.Users GROUP BY Username HAVING COUNT(*) > 1)
    THROW 50004, 'Some users share the same username. Nothing was changed; rename those users first.', 1;
GO
IF OBJECT_ID('dbo.UQ_Users_Username', 'UQ') IS NULL
    ALTER TABLE dbo.Users ADD CONSTRAINT UQ_Users_Username UNIQUE NONCLUSTERED (Username);
GO
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE Name = N'2026-10-08b_users_username_unique')
    INSERT INTO dbo.SchemaMigrations (Name) VALUES (N'2026-10-08b_users_username_unique');
GO
