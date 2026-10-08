-- Unit numbers must be unique within a building (was: only within a floor, so "101" could exist on two floors).
-- Adds UQ_Building_Unit (BuildingId, UnitNumber) and drops UQ_Floor_Unit (FloorId, UnitNumber), which the new rule
-- already covers. Like before, deleted units (IsActive = 0) keep their number.
-- If any building already has the same unit number twice, the script stops and changes nothing.
-- No data is changed. Safe to re-run.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
IF EXISTS (SELECT 1 FROM dbo.Units GROUP BY BuildingId, UnitNumber HAVING COUNT(*) > 1)
    THROW 50002, 'Some buildings have the same unit number more than once. Nothing was changed; rename those units first.', 1;
GO
IF OBJECT_ID('dbo.UQ_Building_Unit', 'UQ') IS NULL
    ALTER TABLE dbo.Units ADD CONSTRAINT UQ_Building_Unit UNIQUE NONCLUSTERED (BuildingId, UnitNumber);
GO
IF OBJECT_ID('dbo.UQ_Building_Unit', 'UQ') IS NOT NULL AND OBJECT_ID('dbo.UQ_Floor_Unit', 'UQ') IS NOT NULL
    ALTER TABLE dbo.Units DROP CONSTRAINT UQ_Floor_Unit;
GO
