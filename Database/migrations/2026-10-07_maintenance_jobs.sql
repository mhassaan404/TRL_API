-- Maintenance module: jobs per building / floor / unit, with a history log.
-- Replaces the old dbo.MaintenanceRequests table, which was never used by the app (no API code). It is dropped
-- only when it is still in the old shape AND empty; if it has rows the script stops and changes nothing.
-- Creates dbo.MaintenanceJobs and dbo.MaintenanceLog. No other table or existing data is changed.
-- Safe to re-run.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
IF OBJECT_ID('dbo.MaintenanceRequests', 'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.MaintenanceRequests)
        THROW 50001, 'dbo.MaintenanceRequests has rows. Nothing was changed; move or delete them first.', 1;
    DROP TABLE dbo.MaintenanceRequests; -- its foreign key to Tenants is dropped with it
END
GO
IF OBJECT_ID('dbo.MaintenanceJobs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MaintenanceJobs (
        Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MaintenanceJobs PRIMARY KEY,
        BuildingId      INT NOT NULL,
        FloorId         INT NULL,
        UnitId          INT NULL,
        TenantId        INT NULL,            -- who it concerns / reported it (usually the unit's tenant); optional
        Title           NVARCHAR(150) NOT NULL,
        Description     NVARCHAR(1000) NULL,
        Category        NVARCHAR(30) NOT NULL,
        Priority        NVARCHAR(10) NOT NULL CONSTRAINT DF_MaintenanceJobs_Priority DEFAULT ('Medium'),
        Status          NVARCHAR(20) NOT NULL CONSTRAINT DF_MaintenanceJobs_Status DEFAULT ('Open'),
        AssignedTo      NVARCHAR(100) NULL,
        ReportedDate    DATE NOT NULL,
        CompletedDate   DATE NULL,
        Cost            DECIMAL(18, 2) NULL, -- owner's cost of the job (tracking only)
        ChargeInvoiceId INT NULL,            -- extra-charge invoice when the cost was billed to the tenant
        MarkedUnit      BIT NOT NULL CONSTRAINT DF_MaintenanceJobs_MarkedUnit DEFAULT (0), -- this job set the unit Under Maintenance
        UnitStatusBefore INT NULL,           -- the unit's status before that, restored when the job is closed
        CreatedBy       INT NULL,
        CreatedAt       DATETIME NOT NULL CONSTRAINT DF_MaintenanceJobs_CreatedAt DEFAULT (GETDATE()),
        UpdatedAt       DATETIME NULL,
        CONSTRAINT CK_MaintenanceJobs_Category CHECK (Category IN ('Plumbing', 'Electrical', 'AC', 'Carpentry', 'Painting', 'Cleaning', 'Other')),
        CONSTRAINT CK_MaintenanceJobs_Priority CHECK (Priority IN ('Low', 'Medium', 'High', 'Urgent')),
        CONSTRAINT CK_MaintenanceJobs_Status CHECK (Status IN ('Open', 'In Progress', 'Completed', 'Cancelled')),
        CONSTRAINT CK_MaintenanceJobs_Cost CHECK (Cost IS NULL OR Cost >= 0),
        CONSTRAINT FK_MaintenanceJobs_Building FOREIGN KEY (BuildingId) REFERENCES dbo.Buildings (BuildingId),
        CONSTRAINT FK_MaintenanceJobs_Floor FOREIGN KEY (FloorId) REFERENCES dbo.Floors (FloorId),
        CONSTRAINT FK_MaintenanceJobs_Unit FOREIGN KEY (UnitId) REFERENCES dbo.Units (UnitId),
        CONSTRAINT FK_MaintenanceJobs_Tenant FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (TenantId),
        CONSTRAINT FK_MaintenanceJobs_ChargeInvoice FOREIGN KEY (ChargeInvoiceId) REFERENCES dbo.RentInvoices (Id)
    );
    CREATE NONCLUSTERED INDEX IX_MaintenanceJobs_Status ON dbo.MaintenanceJobs (Status);
    CREATE NONCLUSTERED INDEX IX_MaintenanceJobs_Unit ON dbo.MaintenanceJobs (UnitId) WHERE UnitId IS NOT NULL;
END
GO
IF OBJECT_ID('dbo.MaintenanceLog', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MaintenanceLog (
        Id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MaintenanceLog PRIMARY KEY,
        JobId     INT NOT NULL,
        Action    NVARCHAR(50) NOT NULL,     -- Created, Updated, Status: <new>, Billed, Unit marked, Unit restored
        Note      NVARCHAR(500) NULL,
        UserId    INT NULL,
        CreatedAt DATETIME NOT NULL CONSTRAINT DF_MaintenanceLog_CreatedAt DEFAULT (GETDATE()),
        CONSTRAINT FK_MaintenanceLog_Job FOREIGN KEY (JobId) REFERENCES dbo.MaintenanceJobs (Id)
    );
    CREATE NONCLUSTERED INDEX IX_MaintenanceLog_Job ON dbo.MaintenanceLog (JobId);
END
GO
