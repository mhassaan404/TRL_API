-- Rent is billed from the dates a lease covered, prorated for partial months.
--   TenantLeases.BilledThrough: last day the lease is billed (inclusive). NULL = still running (incl. month-to-month
--   after EndDate). Set by Terminate (move-out date) and Renew (day before the new lease starts).
--   dbo.LeaseMonthCharge: the covered days and amount of one lease in one month. Used by generation and adjustments.
--   One rent invoice per lease per month (a renewal month can have two prorated invoices, one per lease).
-- Safe to re-run.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('dbo.TenantLeases', 'BilledThrough') IS NULL
    ALTER TABLE dbo.TenantLeases ADD BilledThrough DATE NULL;
GO

-- Backfill ended leases. A renewed lease ends the day before the next lease on the same tenant+unit starts;
-- a terminated lease ends on its termination date. Ended on/before its start day = never occupied (not billed).
UPDATE l
SET BilledThrough = CASE WHEN e.LastDay < l.StartDate THEN DATEADD(DAY, -1, l.StartDate) ELSE e.LastDay END
FROM dbo.TenantLeases l
OUTER APPLY (SELECT TOP 1 n.StartDate FROM dbo.TenantLeases n
             WHERE n.TenantId = l.TenantId AND n.UnitId = l.UnitId AND n.LeaseId > l.LeaseId
             ORDER BY n.LeaseId) nxt
CROSS APPLY (SELECT CASE WHEN l.TerminationReason = 'Renewed' AND nxt.StartDate IS NOT NULL
                         THEN DATEADD(DAY, -1, nxt.StartDate)
                         WHEN CAST(l.TerminatedAt AS DATE) <= l.StartDate THEN DATEADD(DAY, -1, l.StartDate)
                         ELSE CAST(l.TerminatedAt AS DATE) END AS LastDay) e
WHERE l.IsActive = 0 AND l.BilledThrough IS NULL;
GO

IF OBJECT_ID('dbo.CK_TenantLeases_BilledThrough', 'C') IS NULL
    ALTER TABLE dbo.TenantLeases ADD CONSTRAINT CK_TenantLeases_BilledThrough
        CHECK (BilledThrough IS NULL OR BilledThrough >= DATEADD(DAY, -1, StartDate));
GO

-- Covered days and rent of a lease in one month (@MonthStart = first day of the month).
-- Full month = the lease rent; partial month = rent x covered days / days in month, rounded to whole rupees.
CREATE OR ALTER FUNCTION dbo.LeaseMonthCharge (@LeaseId INT, @MonthStart DATE)
RETURNS TABLE
AS
RETURN
    SELECT l.LeaseId, x.FromDate, x.ToDate, x.DaysInMonth,
           CASE WHEN x.Days > 0 THEN x.Days ELSE 0 END AS Days,
           CAST(CASE WHEN x.Days <= 0 THEN 0
                     WHEN x.Days = x.DaysInMonth THEN l.RentAmount
                     ELSE ROUND(l.RentAmount * x.Days / x.DaysInMonth, 0) END AS DECIMAL(18, 2)) AS Amount,
           CASE WHEN x.Days > 0 AND x.Days < x.DaysInMonth
                THEN CONCAT(N'Rent ', FORMAT(x.FromDate, 'dd MMM'), N' - ', FORMAT(x.ToDate, 'dd MMM'),
                            N' (', x.Days, N' of ', x.DaysInMonth, N' days)') END AS Descr
    FROM dbo.TenantLeases l
    CROSS APPLY (SELECT CASE WHEN l.StartDate > @MonthStart THEN l.StartDate ELSE @MonthStart END AS FromDate,
                        CASE WHEN l.BilledThrough < EOMONTH(@MonthStart) THEN l.BilledThrough ELSE EOMONTH(@MonthStart) END AS ToDate,
                        DAY(EOMONTH(@MonthStart)) AS DaysInMonth) d
    CROSS APPLY (SELECT d.FromDate, d.ToDate, d.DaysInMonth, DATEDIFF(DAY, d.FromDate, d.ToDate) + 1 AS Days) x
    WHERE l.LeaseId = @LeaseId;
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_RentInvoices_RentPerUnitMonth' AND object_id = OBJECT_ID('dbo.RentInvoices'))
    DROP INDEX UX_RentInvoices_RentPerUnitMonth ON dbo.RentInvoices;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_RentInvoices_RentPerLeaseMonth' AND object_id = OBJECT_ID('dbo.RentInvoices'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_RentInvoices_RentPerLeaseMonth
        ON dbo.RentInvoices (LeaseId, InvoiceMonth)
        WHERE ChargeType IS NULL AND StatusId <> 6 AND LeaseId IS NOT NULL;
GO
