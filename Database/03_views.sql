-- TRL_DB: views
-- Generated from VICTUS15\SQLEXPRESS (SQL Server 2016). Schema only, no data.

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- Occupancy follows Tenants.UnitId (Tenants is the source of truth); lease columns are informational
CREATE VIEW vw_UnitOccupancy AS
SELECT b.BuildingId, b.BuildingName, f.FloorId, f.FloorNumber, u.UnitId, u.UnitNumber,
       u.BaseRent AS UnitRent,
       tl.LeaseId, t.TenantId, t.Name AS TenantName, tl.RentAmount AS LeaseRent, t.MonthlyRent AS TenantRent,
       tl.StartDate, tl.EndDate, tl.TenureMonths,
       CASE WHEN t.TenantId IS NULL THEN 'Vacant' ELSE 'Occupied' END AS Occupancy
FROM Units u
JOIN Floors f ON f.FloorId = u.FloorId
JOIN Buildings b ON b.BuildingId = f.BuildingId
OUTER APPLY (SELECT TOP 1 TenantId, Name, MonthlyRent FROM Tenants
             WHERE UnitId = u.UnitId AND IsActive = 1 ORDER BY TenantId) t
LEFT JOIN TenantLeases tl ON tl.TenantId = t.TenantId AND tl.UnitId = u.UnitId AND tl.IsActive = 1;
GO

