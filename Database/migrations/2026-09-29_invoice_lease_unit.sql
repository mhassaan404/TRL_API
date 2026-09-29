-- Rent invoices record which lease and unit they bill, and a tenant can be billed rent
-- only once per unit per month. Safe to re-run.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

BEGIN TRAN;

IF COL_LENGTH('dbo.RentInvoices', 'LeaseId') IS NULL
    ALTER TABLE dbo.RentInvoices ADD LeaseId INT NULL
        CONSTRAINT FK_RentInvoices_TenantLeases REFERENCES dbo.TenantLeases (LeaseId);

IF COL_LENGTH('dbo.RentInvoices', 'UnitId') IS NULL
    ALTER TABLE dbo.RentInvoices ADD UnitId INT NULL
        CONSTRAINT FK_RentInvoices_Units REFERENCES dbo.Units (UnitId);

-- First day of the billed month, whatever day InvoiceDate falls on
IF COL_LENGTH('dbo.RentInvoices', 'InvoiceMonth') IS NULL
    ALTER TABLE dbo.RentInvoices ADD InvoiceMonth AS DATEFROMPARTS(YEAR(InvoiceDate), MONTH(InvoiceDate), 1) PERSISTED;

COMMIT;
GO

-- Backfill existing invoices. Prefer the lease that covered the invoice month and whose rent matches the
-- invoice amount, then an active lease, then the most recent one; fall back to the tenant's current unit.
BEGIN TRAN;

UPDATE ri
SET LeaseId = CASE WHEN ri.ChargeType IS NULL THEN m.LeaseId END,
    UnitId  = COALESCE(m.UnitId, t.UnitId)
FROM dbo.RentInvoices ri
JOIN dbo.Tenants t ON t.TenantId = ri.TenantId
OUTER APPLY (
    SELECT TOP 1 l.LeaseId, l.UnitId
    FROM dbo.TenantLeases l
    WHERE l.TenantId = ri.TenantId
      AND l.StartDate < DATEADD(MONTH, 1, ri.InvoiceMonth)
      AND (l.TerminatedAt IS NULL OR l.TerminatedAt >= ri.InvoiceMonth)
    ORDER BY CASE WHEN l.RentAmount = ri.TotalRent THEN 0 ELSE 1 END, l.IsActive DESC, l.StartDate DESC
) m
WHERE ri.UnitId IS NULL;

COMMIT;
GO

-- One live (non-cancelled) rent invoice per tenant, unit and month. Extra charges (ChargeType set) are not limited.
-- Superseded by UX_RentInvoices_RentPerLeaseMonth (2026-09-29b): never recreate it once that exists, because it would
-- block the second prorated invoice in a renewal month.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_RentInvoices_RentPerUnitMonth' AND object_id = OBJECT_ID('dbo.RentInvoices'))
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_RentInvoices_RentPerLeaseMonth' AND object_id = OBJECT_ID('dbo.RentInvoices'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_RentInvoices_RentPerUnitMonth
        ON dbo.RentInvoices (TenantId, UnitId, InvoiceMonth)
        WHERE ChargeType IS NULL AND StatusId <> 6 AND UnitId IS NOT NULL;
GO
