-- Floors had two identical unique constraints on (BuildingId, FloorNumber): UQ_Building_Floor and
-- UQ_Floors_Building_Floor. Keep one. Safe to re-run.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.UQ_Floors_Building_Floor', 'UQ') IS NOT NULL
   AND OBJECT_ID('dbo.UQ_Building_Floor', 'UQ') IS NOT NULL
    ALTER TABLE dbo.Floors DROP CONSTRAINT UQ_Floors_Building_Floor;
GO
