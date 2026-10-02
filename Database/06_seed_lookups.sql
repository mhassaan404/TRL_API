-- TRL_DB: lookup data the application depends on (ids are referenced directly in code, e.g. StatusId 6 = Cancelled).
-- Safe to re-run: rows are only inserted when their id is missing.

SET IDENTITY_INSERT [dbo].[BuildingType] ON;
INSERT INTO [dbo].[BuildingType] ([Id], [Name], [IsActive])
SELECT v.Id, v.Name, 1 FROM (VALUES (1, N'Apartment'), (2, N'House'), (3, N'Office'), (4, N'Shop')) v(Id, Name)
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[BuildingType] t WHERE t.Id = v.Id);
SET IDENTITY_INSERT [dbo].[BuildingType] OFF;
GO

SET IDENTITY_INSERT [dbo].[City] ON;
INSERT INTO [dbo].[City] ([Id], [Name], [IsActive])
SELECT v.Id, v.Name, 1 FROM (VALUES (1, N'Karachi'), (2, N'Lahore'), (3, N'Islamabad'), (4, N'Hyderabad')) v(Id, Name)
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[City] t WHERE t.Id = v.Id);
SET IDENTITY_INSERT [dbo].[City] OFF;
GO

SET IDENTITY_INSERT [dbo].[InvoiceStatus] ON;
INSERT INTO [dbo].[InvoiceStatus] ([Id], [Name], [IsActive])
SELECT v.Id, v.Name, 1 FROM (VALUES (1, N'Pending'), (2, N'Unpaid'), (3, N'Partial'), (4, N'Paid'), (5, N'Overpaid')) v(Id, Name)
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[InvoiceStatus] t WHERE t.Id = v.Id);
SET IDENTITY_INSERT [dbo].[InvoiceStatus] OFF;
GO

SET IDENTITY_INSERT [dbo].[StatusList] ON;
INSERT INTO [dbo].[StatusList] ([StatusId], [StatusName], [IsActive])
SELECT v.Id, v.Name, 1 FROM (VALUES (1, N'Paid'), (2, N'Unpaid'), (3, N'Pending'), (4, N'In Progress'), (5, N'Completed'),
                                    (6, N'Cancelled'), (8, N'Partial'), (9, N'Overpaid')) v(Id, Name)
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[StatusList] t WHERE t.StatusId = v.Id);
SET IDENTITY_INSERT [dbo].[StatusList] OFF;
GO

SET IDENTITY_INSERT [dbo].[UnitStatus] ON;
INSERT INTO [dbo].[UnitStatus] ([Id], [Name], [IsActive])
SELECT v.Id, v.Name, 1 FROM (VALUES (1, N'Available'), (2, N'Rented'), (3, N'Reserved'), (4, N'Under Maintenance')) v(Id, Name)
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[UnitStatus] t WHERE t.Id = v.Id);
SET IDENTITY_INSERT [dbo].[UnitStatus] OFF;
GO

-- Late fee settings (one row; edited on the Late Fee Settings page): 5 due days, 500 per day, max 2 x invoice rent
INSERT INTO [dbo].[LateFeeSettings] ([Id], [PaymentDueDays], [LateFeePerDay], [MaxLateFeeMultiplier])
SELECT 1, 5, 500, 2 WHERE NOT EXISTS (SELECT 1 FROM [dbo].[LateFeeSettings] WHERE [Id] = 1);
GO
