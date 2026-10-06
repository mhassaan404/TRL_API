-- TRL_DB: foreign keys
-- Generated from VICTUS15\SQLEXPRESS (SQL Server 2016). Schema only, no data.

ALTER TABLE [dbo].[Buildings]  WITH CHECK ADD  CONSTRAINT [FK_Building_City] FOREIGN KEY([CityId])
REFERENCES [dbo].[City] ([Id])
GO
ALTER TABLE [dbo].[Buildings] CHECK CONSTRAINT [FK_Building_City]
GO

ALTER TABLE [dbo].[Buildings]  WITH CHECK ADD  CONSTRAINT [FK_Building_Type] FOREIGN KEY([TypeId])
REFERENCES [dbo].[BuildingType] ([Id])
GO
ALTER TABLE [dbo].[Buildings] CHECK CONSTRAINT [FK_Building_Type]
GO

ALTER TABLE [dbo].[Floors]  WITH CHECK ADD  CONSTRAINT [FK_Floors_Buildings] FOREIGN KEY([BuildingId])
REFERENCES [dbo].[Buildings] ([BuildingId])
GO
ALTER TABLE [dbo].[Floors] CHECK CONSTRAINT [FK_Floors_Buildings]
GO

ALTER TABLE [dbo].[MaintenanceRequests]  WITH CHECK ADD  CONSTRAINT [FK__Maintenan__Tenan__1FCDBCEB] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
ON UPDATE CASCADE
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[MaintenanceRequests] CHECK CONSTRAINT [FK__Maintenan__Tenan__1FCDBCEB]
GO

ALTER TABLE [dbo].[Payments]  WITH CHECK ADD  CONSTRAINT [FK__Payments__Tenant__182C9B23] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
ON UPDATE CASCADE
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[Payments] CHECK CONSTRAINT [FK__Payments__Tenant__182C9B23]
GO

ALTER TABLE [dbo].[Payments]  WITH CHECK ADD  CONSTRAINT [FK_Payments_RentInvoice] FOREIGN KEY([RentInvoiceId])
REFERENCES [dbo].[RentInvoices] ([Id])
GO
ALTER TABLE [dbo].[Payments] CHECK CONSTRAINT [FK_Payments_RentInvoice]
GO

ALTER TABLE [dbo].[RefreshTokens]  WITH CHECK ADD  CONSTRAINT [FK_RefreshTokens_Users] FOREIGN KEY([UserId])
REFERENCES [dbo].[Users] ([UserId])
GO
ALTER TABLE [dbo].[RefreshTokens] CHECK CONSTRAINT [FK_RefreshTokens_Users]
GO

ALTER TABLE [dbo].[RentInvoices]  WITH CHECK ADD  CONSTRAINT [FK_RentInvoices_RelatedInvoice] FOREIGN KEY([RelatedInvoiceId])
REFERENCES [dbo].[RentInvoices] ([Id])
GO
ALTER TABLE [dbo].[RentInvoices] CHECK CONSTRAINT [FK_RentInvoices_RelatedInvoice]
GO

ALTER TABLE [dbo].[RentInvoices]  WITH CHECK ADD  CONSTRAINT [FK_RentInvoices_TenantLeases] FOREIGN KEY([LeaseId])
REFERENCES [dbo].[TenantLeases] ([LeaseId])
GO
ALTER TABLE [dbo].[RentInvoices] CHECK CONSTRAINT [FK_RentInvoices_TenantLeases]
GO

ALTER TABLE [dbo].[RentInvoices]  WITH CHECK ADD  CONSTRAINT [FK_RentInvoices_Units] FOREIGN KEY([UnitId])
REFERENCES [dbo].[Units] ([UnitId])
GO
ALTER TABLE [dbo].[RentInvoices] CHECK CONSTRAINT [FK_RentInvoices_Units]
GO

ALTER TABLE [dbo].[RentPayments]  WITH CHECK ADD FOREIGN KEY([RentInvoiceId])
REFERENCES [dbo].[RentInvoices] ([Id])
GO

ALTER TABLE [dbo].[TenantLeases]  WITH CHECK ADD  CONSTRAINT [FK_TenantLeases_Tenant] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[TenantLeases] CHECK CONSTRAINT [FK_TenantLeases_Tenant]
GO

ALTER TABLE [dbo].[TenantLeases]  WITH CHECK ADD  CONSTRAINT [FK_TenantLeases_Unit] FOREIGN KEY([UnitId])
REFERENCES [dbo].[Units] ([UnitId])
GO
ALTER TABLE [dbo].[TenantLeases] CHECK CONSTRAINT [FK_TenantLeases_Unit]
GO

ALTER TABLE [dbo].[Tenants]  WITH CHECK ADD  CONSTRAINT [FK_Tenant_City] FOREIGN KEY([CityId])
REFERENCES [dbo].[City] ([Id])
GO
ALTER TABLE [dbo].[Tenants] CHECK CONSTRAINT [FK_Tenant_City]
GO

ALTER TABLE [dbo].[Tenants]  WITH CHECK ADD  CONSTRAINT [FK_Tenants_Buildings] FOREIGN KEY([BuildingId])
REFERENCES [dbo].[Buildings] ([BuildingId])
GO
ALTER TABLE [dbo].[Tenants] CHECK CONSTRAINT [FK_Tenants_Buildings]
GO

ALTER TABLE [dbo].[Tenants]  WITH CHECK ADD  CONSTRAINT [FK_Tenants_Floors] FOREIGN KEY([FloorId])
REFERENCES [dbo].[Floors] ([FloorId])
GO
ALTER TABLE [dbo].[Tenants] CHECK CONSTRAINT [FK_Tenants_Floors]
GO

ALTER TABLE [dbo].[Tenants]  WITH CHECK ADD  CONSTRAINT [FK_Tenants_Units] FOREIGN KEY([UnitId])
REFERENCES [dbo].[Units] ([UnitId])
GO
ALTER TABLE [dbo].[Tenants] CHECK CONSTRAINT [FK_Tenants_Units]
GO

ALTER TABLE [dbo].[Units]  WITH CHECK ADD  CONSTRAINT [FK_Unit_Status] FOREIGN KEY([StatusId])
REFERENCES [dbo].[UnitStatus] ([Id])
GO
ALTER TABLE [dbo].[Units] CHECK CONSTRAINT [FK_Unit_Status]
GO

ALTER TABLE [dbo].[Units]  WITH CHECK ADD  CONSTRAINT [FK_Units_Buildings] FOREIGN KEY([BuildingId])
REFERENCES [dbo].[Buildings] ([BuildingId])
GO
ALTER TABLE [dbo].[Units] CHECK CONSTRAINT [FK_Units_Buildings]
GO

ALTER TABLE [dbo].[Units]  WITH CHECK ADD  CONSTRAINT [FK_Units_Floors] FOREIGN KEY([FloorId])
REFERENCES [dbo].[Floors] ([FloorId])
GO
ALTER TABLE [dbo].[Units] CHECK CONSTRAINT [FK_Units_Floors]
GO

