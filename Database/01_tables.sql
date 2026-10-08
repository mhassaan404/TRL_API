-- TRL_DB: tables, primary keys, indexes, defaults, checks
-- Generated from VICTUS15\SQLEXPRESS (SQL Server 2016). Schema only, no data.

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Buildings](
	[BuildingId] [int] IDENTITY(1,1) NOT NULL,
	[BuildingName] [nvarchar](100) NOT NULL,
	[CityId] [int] NULL,
	[TypeId] [int] NULL,
	[Address] [nvarchar](200) NULL,
	[IsActive] [bit] NULL,
 CONSTRAINT [PK__Building__5463CDC45D210E37] PRIMARY KEY CLUSTERED 
(
	[BuildingId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY],
 CONSTRAINT [UQ__Building__2D13A8E2518E9D11] UNIQUE NONCLUSTERED 
(
	[BuildingName] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[BuildingType](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Name] [varchar](50) NOT NULL,
	[IsActive] [bit] NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
ALTER TABLE [dbo].[BuildingType] ADD  DEFAULT ((1)) FOR [IsActive]
GO

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[City](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Name] [varchar](100) NOT NULL,
	[IsActive] [bit] NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
ALTER TABLE [dbo].[City] ADD  DEFAULT ((1)) FOR [IsActive]
GO

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Floors](
	[FloorId] [int] IDENTITY(1,1) NOT NULL,
	[BuildingId] [int] NOT NULL,
	[FloorNumber] [nvarchar](50) NOT NULL,
	[IsActive] [bit] NULL,
PRIMARY KEY CLUSTERED 
(
	[FloorId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY],
 CONSTRAINT [UQ_Building_Floor] UNIQUE NONCLUSTERED 
(
	[BuildingId] ASC,
	[FloorNumber] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[InvoiceAudit](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[InvoiceId] [int] NOT NULL,
	[Action] [varchar](40) NOT NULL,
	[Amount] [decimal](18, 2) NULL,
	[Reason] [nvarchar](300) NULL,
	[CreatedBy] [int] NULL,
	[CreatedAt] [datetime] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
ALTER TABLE [dbo].[InvoiceAudit] ADD  DEFAULT (getdate()) FOR [CreatedAt]
GO

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[InvoiceStatus](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](50) NOT NULL,
	[IsActive] [bit] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
ALTER TABLE [dbo].[InvoiceStatus] ADD  DEFAULT ((1)) FOR [IsActive]
GO

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[LateFeeSettings](
	[Id] [int] NOT NULL,
	[PaymentDueDays] [int] NOT NULL,
	[LateFeePerDay] [decimal](18, 2) NOT NULL,
	[MaxLateFeeMultiplier] [decimal](5, 2) NOT NULL,
	[UpdatedBy] [int] NULL,
	[UpdatedAt] [datetime] NULL,
 CONSTRAINT [PK_LateFeeSettings] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
ALTER TABLE [dbo].[LateFeeSettings]  WITH CHECK ADD  CONSTRAINT [CK_LateFeeSettings_LateFeePerDay] CHECK  (([LateFeePerDay]>(0) AND [LateFeePerDay]<=(100000)))
GO
ALTER TABLE [dbo].[LateFeeSettings] CHECK CONSTRAINT [CK_LateFeeSettings_LateFeePerDay]
GO
ALTER TABLE [dbo].[LateFeeSettings]  WITH CHECK ADD  CONSTRAINT [CK_LateFeeSettings_MaxLateFeeMultiplier] CHECK  (([MaxLateFeeMultiplier]>(0) AND [MaxLateFeeMultiplier]<=(12)))
GO
ALTER TABLE [dbo].[LateFeeSettings] CHECK CONSTRAINT [CK_LateFeeSettings_MaxLateFeeMultiplier]
GO
ALTER TABLE [dbo].[LateFeeSettings]  WITH CHECK ADD  CONSTRAINT [CK_LateFeeSettings_PaymentDueDays] CHECK  (([PaymentDueDays]>=(0) AND [PaymentDueDays]<=(90)))
GO
ALTER TABLE [dbo].[LateFeeSettings] CHECK CONSTRAINT [CK_LateFeeSettings_PaymentDueDays]
GO
ALTER TABLE [dbo].[LateFeeSettings]  WITH CHECK ADD  CONSTRAINT [CK_LateFeeSettings_SingleRow] CHECK  (([Id]=(1)))
GO
ALTER TABLE [dbo].[LateFeeSettings] CHECK CONSTRAINT [CK_LateFeeSettings_SingleRow]
GO

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[MaintenanceJobs](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[BuildingId] [int] NOT NULL,
	[FloorId] [int] NULL,
	[UnitId] [int] NULL,
	[TenantId] [int] NULL,
	[Title] [nvarchar](150) NOT NULL,
	[Description] [nvarchar](1000) NULL,
	[Category] [nvarchar](30) NOT NULL,
	[Priority] [nvarchar](10) NOT NULL,
	[Status] [nvarchar](20) NOT NULL,
	[AssignedTo] [nvarchar](100) NULL,
	[ReportedDate] [date] NOT NULL,
	[CompletedDate] [date] NULL,
	[Cost] [decimal](18, 2) NULL,
	[ChargeInvoiceId] [int] NULL,
	[MarkedUnit] [bit] NOT NULL,
	[UnitStatusBefore] [int] NULL,
	[CreatedBy] [int] NULL,
	[CreatedAt] [datetime] NOT NULL,
	[UpdatedAt] [datetime] NULL,
 CONSTRAINT [PK_MaintenanceJobs] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE NONCLUSTERED INDEX [IX_MaintenanceJobs_Status] ON [dbo].[MaintenanceJobs]
(
	[Status] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_MaintenanceJobs_Unit] ON [dbo].[MaintenanceJobs]
(
	[UnitId] ASC
)
WHERE ([UnitId] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
ALTER TABLE [dbo].[MaintenanceJobs] ADD  CONSTRAINT [DF_MaintenanceJobs_Priority]  DEFAULT ('Medium') FOR [Priority]
GO
ALTER TABLE [dbo].[MaintenanceJobs] ADD  CONSTRAINT [DF_MaintenanceJobs_Status]  DEFAULT ('Open') FOR [Status]
GO
ALTER TABLE [dbo].[MaintenanceJobs] ADD  CONSTRAINT [DF_MaintenanceJobs_MarkedUnit]  DEFAULT ((0)) FOR [MarkedUnit]
GO
ALTER TABLE [dbo].[MaintenanceJobs] ADD  CONSTRAINT [DF_MaintenanceJobs_CreatedAt]  DEFAULT (getdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[MaintenanceJobs]  WITH CHECK ADD  CONSTRAINT [CK_MaintenanceJobs_Category] CHECK  (([Category]='Other' OR [Category]='Cleaning' OR [Category]='Painting' OR [Category]='Carpentry' OR [Category]='AC' OR [Category]='Electrical' OR [Category]='Plumbing'))
GO
ALTER TABLE [dbo].[MaintenanceJobs] CHECK CONSTRAINT [CK_MaintenanceJobs_Category]
GO
ALTER TABLE [dbo].[MaintenanceJobs]  WITH CHECK ADD  CONSTRAINT [CK_MaintenanceJobs_Cost] CHECK  (([Cost] IS NULL OR [Cost]>=(0)))
GO
ALTER TABLE [dbo].[MaintenanceJobs] CHECK CONSTRAINT [CK_MaintenanceJobs_Cost]
GO
ALTER TABLE [dbo].[MaintenanceJobs]  WITH CHECK ADD  CONSTRAINT [CK_MaintenanceJobs_Priority] CHECK  (([Priority]='Urgent' OR [Priority]='High' OR [Priority]='Medium' OR [Priority]='Low'))
GO
ALTER TABLE [dbo].[MaintenanceJobs] CHECK CONSTRAINT [CK_MaintenanceJobs_Priority]
GO
ALTER TABLE [dbo].[MaintenanceJobs]  WITH CHECK ADD  CONSTRAINT [CK_MaintenanceJobs_Status] CHECK  (([Status]='Cancelled' OR [Status]='Completed' OR [Status]='In Progress' OR [Status]='Open'))
GO
ALTER TABLE [dbo].[MaintenanceJobs] CHECK CONSTRAINT [CK_MaintenanceJobs_Status]
GO

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[MaintenanceLog](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[JobId] [int] NOT NULL,
	[Action] [nvarchar](50) NOT NULL,
	[Note] [nvarchar](500) NULL,
	[UserId] [int] NULL,
	[CreatedAt] [datetime] NOT NULL,
 CONSTRAINT [PK_MaintenanceLog] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_MaintenanceLog_Job] ON [dbo].[MaintenanceLog]
(
	[JobId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
ALTER TABLE [dbo].[MaintenanceLog] ADD  CONSTRAINT [DF_MaintenanceLog_CreatedAt]  DEFAULT (getdate()) FOR [CreatedAt]
GO

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Payments](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PaymentAmount] [decimal](10, 2) NOT NULL,
	[PaymentDate] [datetime] NULL,
	[RentInvoiceId] [int] NULL,
	[PaymentMethod] [varchar](150) NULL,
	[CreatedAt] [datetime] NULL,
	[CreatedBy] [int] NULL,
	[UpdatedAt] [datetime] NULL,
	[UpdatedBy] [int] NULL,
	[Notes] [nvarchar](500) NULL,
	[DiscountAmount] [decimal](18, 2) NOT NULL,
	[DiscountPercent] [decimal](5, 2) NOT NULL,
	[IsLateFeeWaived] [bit] NOT NULL,
 CONSTRAINT [PK__Payments__3214EC075A09B4C7] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_Payments_Invoice] ON [dbo].[Payments]
(
	[RentInvoiceId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
ALTER TABLE [dbo].[Payments] ADD  CONSTRAINT [DF__Payments__Create__173876EA]  DEFAULT (getdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[Payments] ADD  CONSTRAINT [DF__Payments__Discou__2CBDA3B5]  DEFAULT ((0)) FOR [DiscountAmount]
GO
ALTER TABLE [dbo].[Payments] ADD  CONSTRAINT [DF__Payments__Discou__2DB1C7EE]  DEFAULT ((0)) FOR [DiscountPercent]
GO
ALTER TABLE [dbo].[Payments] ADD  CONSTRAINT [DF__Payments__LateFe__2EA5EC27]  DEFAULT ((0)) FOR [IsLateFeeWaived]
GO

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[RefreshTokens](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Token] [nvarchar](500) NOT NULL,
	[Expires] [datetime2](7) NOT NULL,
	[IsRevoked] [bit] NOT NULL,
	[UserId] [int] NOT NULL,
	[CreatedAt] [datetime2](7) NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
ALTER TABLE [dbo].[RefreshTokens] ADD  DEFAULT ((0)) FOR [IsRevoked]
GO

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[RentInvoices](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[TotalRent] [decimal](18, 2) NOT NULL,
	[PendingAmount] [decimal](18, 2) NOT NULL,
	[CreatedAt] [datetime] NOT NULL,
	[DueDate] [date] NULL,
	[StatusId] [int] NULL,
	[OverPaidAmount] [decimal](18, 2) NULL,
	[InvoiceDate] [date] NULL,
	[DiscountAmount] [decimal](18, 2) NOT NULL,
	[IsLateFeeWaived] [bit] NOT NULL,
	[UpdatedAt] [datetime] NULL,
	[Description] [nvarchar](255) NULL,
	[ChargeType] [nvarchar](50) NULL,
	[LateFeeCharged] [decimal](18, 2) NOT NULL,
	[LateFeeChargedAt] [datetime] NULL,
	[LeaseId] [int] NULL,
	[UnitId] [int] NULL,
	[InvoiceMonth]  AS (datefromparts(datepart(year,[InvoiceDate]),datepart(month,[InvoiceDate]),(1))) PERSISTED,
	[LateFeePerDay] [decimal](18, 2) NOT NULL,
	[LateFeeMaxMultiplier] [decimal](5, 2) NOT NULL,
	[RelatedInvoiceId] [int] NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_RentInvoices_RelatedInvoice] ON [dbo].[RentInvoices]
(
	[RelatedInvoiceId] ASC
)
WHERE ([RelatedInvoiceId] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_RentInvoices_Tenant] ON [dbo].[RentInvoices]
(
	[TenantId] ASC,
	[StatusId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_RentInvoices_TenantId] ON [dbo].[RentInvoices]
(
	[TenantId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ARITHABORT ON
SET CONCAT_NULL_YIELDS_NULL ON
SET QUOTED_IDENTIFIER ON
SET ANSI_NULLS ON
SET ANSI_PADDING ON
SET ANSI_WARNINGS ON
SET NUMERIC_ROUNDABORT OFF
GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_RentInvoices_RentPerLeaseMonth] ON [dbo].[RentInvoices]
(
	[LeaseId] ASC,
	[InvoiceMonth] ASC
)
WHERE ([ChargeType] IS NULL AND [StatusId]<>(6) AND [LeaseId] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
ALTER TABLE [dbo].[RentInvoices] ADD  DEFAULT ((0)) FOR [PendingAmount]
GO
ALTER TABLE [dbo].[RentInvoices] ADD  DEFAULT (getdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[RentInvoices] ADD  DEFAULT ((0)) FOR [DiscountAmount]
GO
ALTER TABLE [dbo].[RentInvoices] ADD  DEFAULT ((0)) FOR [IsLateFeeWaived]
GO
ALTER TABLE [dbo].[RentInvoices] ADD  CONSTRAINT [DF_RI_LFC]  DEFAULT ((0)) FOR [LateFeeCharged]
GO

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[RentPayments](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[RentInvoiceId] [int] NOT NULL,
	[TenantId] [int] NOT NULL,
	[PaymentAmount] [decimal](18, 2) NOT NULL,
	[DiscountAmount] [decimal](18, 2) NOT NULL,
	[IsLateFeeWaived] [bit] NOT NULL,
	[PaymentMethod] [nvarchar](50) NULL,
	[PaymentDate] [date] NULL,
	[Notes] [nvarchar](500) NULL,
	[CreatedAt] [datetime] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
ALTER TABLE [dbo].[RentPayments] ADD  DEFAULT ((0)) FOR [PaymentAmount]
GO
ALTER TABLE [dbo].[RentPayments] ADD  DEFAULT ((0)) FOR [DiscountAmount]
GO
ALTER TABLE [dbo].[RentPayments] ADD  DEFAULT ((0)) FOR [IsLateFeeWaived]
GO
ALTER TABLE [dbo].[RentPayments] ADD  DEFAULT (getdate()) FOR [CreatedAt]
GO

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[SchemaMigrations](
	[Name] [nvarchar](200) NOT NULL,
	[AppliedAt] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_SchemaMigrations] PRIMARY KEY CLUSTERED 
(
	[Name] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
ALTER TABLE [dbo].[SchemaMigrations] ADD  CONSTRAINT [DF_SchemaMigrations_AppliedAt]  DEFAULT (sysutcdatetime()) FOR [AppliedAt]
GO

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[StatusList](
	[StatusId] [int] IDENTITY(1,1) NOT NULL,
	[StatusName] [varchar](50) NOT NULL,
	[IsActive] [bit] NULL,
 CONSTRAINT [PK_StatusList] PRIMARY KEY CLUSTERED 
(
	[StatusId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
ALTER TABLE [dbo].[StatusList] ADD  CONSTRAINT [DF__StatusLis__IsAct__36B12243]  DEFAULT ((1)) FOR [IsActive]
GO

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[TenantLeases](
	[LeaseId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[UnitId] [int] NOT NULL,
	[RentAmount] [decimal](18, 2) NOT NULL,
	[StartDate] [date] NOT NULL,
	[TenureMonths] [int] NOT NULL,
	[EndDate]  AS (dateadd(month,[TenureMonths],[StartDate])) PERSISTED,
	[IsActive] [bit] NOT NULL,
	[TerminatedAt] [datetime] NULL,
	[TerminationReason] [nvarchar](300) NULL,
	[CreatedBy] [int] NULL,
	[CreatedAt] [datetime] NOT NULL,
	[BilledThrough] [date] NULL,
PRIMARY KEY CLUSTERED 
(
	[LeaseId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_TenantLeases_Tenant] ON [dbo].[TenantLeases]
(
	[TenantId] ASC,
	[IsActive] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_TenantLeases_ActiveUnit] ON [dbo].[TenantLeases]
(
	[UnitId] ASC
)
WHERE ([IsActive]=(1))
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
ALTER TABLE [dbo].[TenantLeases] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[TenantLeases] ADD  DEFAULT (getdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[TenantLeases]  WITH CHECK ADD  CONSTRAINT [CK_TenantLeases_BilledThrough] CHECK  (([BilledThrough] IS NULL OR [BilledThrough]>=dateadd(day,(-1),[StartDate])))
GO
ALTER TABLE [dbo].[TenantLeases] CHECK CONSTRAINT [CK_TenantLeases_BilledThrough]
GO

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Tenants](
	[TenantId] [int] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](150) NOT NULL,
	[BuildingId] [int] NULL,
	[FloorId] [int] NULL,
	[UnitId] [int] NULL,
	[Contact] [nvarchar](50) NULL,
	[Email] [varchar](100) NULL,
	[MonthlyRent] [decimal](18, 2) NULL,
	[MoveOutDate] [datetime] NULL,
	[CityId] [int] NULL,
	[CreatedBy] [varchar](150) NULL,
	[CreatedAt] [datetime] NULL,
	[UpdatedBy] [varchar](150) NULL,
	[UpdatedAt] [datetime] NULL,
	[Notes] [nvarchar](max) NULL,
	[IsActive] [bit] NULL,
	[IsDeleted] [bit] NOT NULL,
	[TenantType] [nvarchar](20) NOT NULL,
	[ContactPerson] [nvarchar](150) NULL,
	[CnicNtn] [nvarchar](20) NULL,
	[Address] [nvarchar](500) NULL,
 CONSTRAINT [PK__Tenants__3214EC077E9072C9] PRIMARY KEY CLUSTERED 
(
	[TenantId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_Tenants_Building_Floor_Unit] ON [dbo].[Tenants]
(
	[BuildingId] ASC,
	[FloorId] ASC,
	[UnitId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
ALTER TABLE [dbo].[Tenants] ADD  CONSTRAINT [DF__Tenants__Created__108B795B]  DEFAULT (getdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[Tenants] ADD  CONSTRAINT [DF_Tenants_IsDeleted]  DEFAULT ((0)) FOR [IsDeleted]
GO
ALTER TABLE [dbo].[Tenants] ADD  CONSTRAINT [DF_Tenants_TenantType]  DEFAULT (N'Individual') FOR [TenantType]
GO
ALTER TABLE [dbo].[Tenants]  WITH CHECK ADD  CONSTRAINT [CK_Tenants_TenantType] CHECK  (([TenantType]=N'Individual' OR [TenantType]=N'Company'))
GO
ALTER TABLE [dbo].[Tenants] CHECK CONSTRAINT [CK_Tenants_TenantType]
GO

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Units](
	[UnitId] [int] IDENTITY(1,1) NOT NULL,
	[FloorId] [int] NOT NULL,
	[UnitNumber] [nvarchar](50) NOT NULL,
	[BuildingId] [int] NOT NULL,
	[StatusId] [int] NULL,
	[BaseRent] [decimal](18, 2) NOT NULL,
	[IsActive] [bit] NULL,
	[Note] [varchar](255) NULL,
	[PropertyType] [nvarchar](50) NULL,
 CONSTRAINT [PK__Units__44F5ECB56B82DD82] PRIMARY KEY CLUSTERED 
(
	[UnitId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY],
 CONSTRAINT [UQ_Building_Unit] UNIQUE NONCLUSTERED 
(
	[BuildingId] ASC,
	[UnitNumber] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
ALTER TABLE [dbo].[Units] ADD  CONSTRAINT [DF__Units__BaseRent__07C12930]  DEFAULT ((0)) FOR [BaseRent]
GO

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[UnitStatus](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Name] [varchar](50) NOT NULL,
	[IsActive] [bit] NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
ALTER TABLE [dbo].[UnitStatus] ADD  DEFAULT ((1)) FOR [IsActive]
GO

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Users](
	[UserId] [int] IDENTITY(1,1) NOT NULL,
	[Username] [nvarchar](100) NOT NULL,
	[Email] [nvarchar](100) NOT NULL,
	[Phone] [nvarchar](20) NULL,
	[PasswordHash] [nvarchar](255) NOT NULL,
	[Role] [nvarchar](50) NOT NULL,
	[CreatedAt] [datetime] NULL,
	[IsActive] [bit] NULL,
PRIMARY KEY CLUSTERED 
(
	[UserId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY],
UNIQUE NONCLUSTERED 
(
	[Email] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY],
 CONSTRAINT [UQ_Users_Username] UNIQUE NONCLUSTERED 
(
	[Username] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
ALTER TABLE [dbo].[Users] ADD  DEFAULT (getdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[Users] ADD  DEFAULT ((1)) FOR [IsActive]
GO

