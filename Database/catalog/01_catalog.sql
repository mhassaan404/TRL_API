-- TRL client catalog (run once on the catalog database, e.g. TRL_Catalog). Safe to re-run.
-- One row per client. No business data and no passwords/connection strings are stored here: each client's
-- connection string lives in the API's server settings under ClientConnections:<ConnectionKey>
-- (environment variable ClientConnections__<ConnectionKey>). Add clients with TRL_Tools ("provision"/"register").
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
IF OBJECT_ID('dbo.Clients', 'U') IS NULL
    CREATE TABLE dbo.Clients (
        ClientId      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Clients PRIMARY KEY,
        ClientCode    NVARCHAR(20) NOT NULL CONSTRAINT UQ_Clients_Code UNIQUE,         -- typed at login, e.g. TRL
        ClientName    NVARCHAR(150) NOT NULL,                                          -- shown in the app header
        DatabaseName  NVARCHAR(128) NOT NULL,                                          -- for reference/tools
        ConnectionKey NVARCHAR(50) NOT NULL CONSTRAINT UQ_Clients_ConnectionKey UNIQUE, -- ClientConnections:<key>
        IsActive      BIT NOT NULL CONSTRAINT DF_Clients_IsActive DEFAULT (1),
        CreatedAt     DATETIME2 NOT NULL CONSTRAINT DF_Clients_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT CK_Clients_Code CHECK (ClientCode NOT LIKE '%[^A-Za-z0-9_-]%' AND LEN(ClientCode) >= 2),
        CONSTRAINT CK_Clients_ConnectionKey CHECK (ConnectionKey NOT LIKE '%[^A-Za-z0-9_]%' AND LEN(ConnectionKey) >= 1)
    );
GO
