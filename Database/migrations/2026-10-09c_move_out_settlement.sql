-- Move-out settlement: closes a tenancy after its lease has ended.
--   dbo.MoveOutSettlements            one per ended lease (UX on LeaseId): the figures at settlement time
--   dbo.MoveOutSettlementDeductions   justified deductions (damage, cleaning, ...) with a reason; each one is billed as
--                                     an extra-charge invoice (InvoiceId) so it stays in the invoice history
-- Deposit ledger (dbo.SecurityDeposits) gets three entry types, all linked to the settlement:
--   Credit Transfer (+)  an overpaid invoice's credit moved into the deposit pool
--   Applied (-)          deposit used to pay an invoice (InvoiceId; matched by a Payments row, method 'Security Deposit')
--   Refund (-)           deposit money actually paid back to the tenant (method required)
-- No existing data is changed. Safe to re-run.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
IF OBJECT_ID('dbo.MoveOutSettlements', 'U') IS NULL
    CREATE TABLE dbo.MoveOutSettlements (
        Id                   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MoveOutSettlements PRIMARY KEY,
        TenantId             INT            NOT NULL,
        UnitId               INT            NOT NULL,
        LeaseId              INT            NOT NULL,
        SettlementDate       DATE           NOT NULL,
        OutstandingBefore    DECIMAL(18, 2) NOT NULL,
        CreditBefore         DECIMAL(18, 2) NOT NULL,
        DeductionsTotal      DECIMAL(18, 2) NOT NULL,
        DepositHeldBefore    DECIMAL(18, 2) NOT NULL,
        DepositApplied       DECIMAL(18, 2) NOT NULL,
        TenantOwes           DECIMAL(18, 2) NOT NULL,
        RefundDue            DECIMAL(18, 2) NOT NULL,
        FinalPaymentReceived DECIMAL(18, 2) NOT NULL CONSTRAINT DF_MoveOutSettlements_FinalPayment DEFAULT (0),
        RefundPaid           DECIMAL(18, 2) NOT NULL CONSTRAINT DF_MoveOutSettlements_RefundPaid DEFAULT (0),
        Notes                NVARCHAR(500)  NULL,
        CreatedBy            INT            NULL,
        CreatedAt            DATETIME       NOT NULL CONSTRAINT DF_MoveOutSettlements_CreatedAt DEFAULT (GETDATE()),
        CONSTRAINT CK_MoveOutSettlements_Amounts CHECK (
            OutstandingBefore >= 0 AND CreditBefore >= 0 AND DeductionsTotal >= 0 AND DepositHeldBefore >= 0
            AND DepositApplied >= 0 AND TenantOwes >= 0 AND RefundDue >= 0 AND FinalPaymentReceived >= 0 AND RefundPaid >= 0),
        CONSTRAINT CK_MoveOutSettlements_Applied CHECK (DepositApplied <= DepositHeldBefore + CreditBefore),
        CONSTRAINT CK_MoveOutSettlements_Paid CHECK (FinalPaymentReceived <= TenantOwes AND RefundPaid <= RefundDue)
    );
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_MoveOutSettlements_Lease' AND object_id = OBJECT_ID('dbo.MoveOutSettlements'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_MoveOutSettlements_Lease ON dbo.MoveOutSettlements (LeaseId);
GO
IF OBJECT_ID('dbo.FK_MoveOutSettlements_Tenant', 'F') IS NULL
    ALTER TABLE dbo.MoveOutSettlements ADD CONSTRAINT FK_MoveOutSettlements_Tenant FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (TenantId);
IF OBJECT_ID('dbo.FK_MoveOutSettlements_Unit', 'F') IS NULL
    ALTER TABLE dbo.MoveOutSettlements ADD CONSTRAINT FK_MoveOutSettlements_Unit FOREIGN KEY (UnitId) REFERENCES dbo.Units (UnitId);
IF OBJECT_ID('dbo.FK_MoveOutSettlements_Lease', 'F') IS NULL
    ALTER TABLE dbo.MoveOutSettlements ADD CONSTRAINT FK_MoveOutSettlements_Lease FOREIGN KEY (LeaseId) REFERENCES dbo.TenantLeases (LeaseId);
GO
IF OBJECT_ID('dbo.MoveOutSettlementDeductions', 'U') IS NULL
    CREATE TABLE dbo.MoveOutSettlementDeductions (
        Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MoveOutSettlementDeductions PRIMARY KEY,
        SettlementId INT            NOT NULL,
        ChargeType   NVARCHAR(50)   NOT NULL,
        Amount       DECIMAL(18, 2) NOT NULL,
        Reason       NVARCHAR(255)  NOT NULL,
        InvoiceId    INT            NOT NULL,
        CONSTRAINT CK_MoveOutSettlementDeductions_Type CHECK (ChargeType IN ('Damage', 'Cleaning', 'Maintenance', 'Utility', 'Other')),
        CONSTRAINT CK_MoveOutSettlementDeductions_Amount CHECK (Amount > 0 AND Amount <= 1000000000),
        CONSTRAINT CK_MoveOutSettlementDeductions_Reason CHECK (LEN(Reason) > 0)
    );
GO
IF OBJECT_ID('dbo.FK_MoveOutSettlementDeductions_Settlement', 'F') IS NULL
    ALTER TABLE dbo.MoveOutSettlementDeductions ADD CONSTRAINT FK_MoveOutSettlementDeductions_Settlement FOREIGN KEY (SettlementId) REFERENCES dbo.MoveOutSettlements (Id);
IF OBJECT_ID('dbo.FK_MoveOutSettlementDeductions_Invoice', 'F') IS NULL
    ALTER TABLE dbo.MoveOutSettlementDeductions ADD CONSTRAINT FK_MoveOutSettlementDeductions_Invoice FOREIGN KEY (InvoiceId) REFERENCES dbo.RentInvoices (Id);
GO
-- One line per invoice settled, as it stood at settlement (for the statement, which must not change later):
-- LineType Outstanding (owed), Credit (overpaid, moved to the deposit) or Deduction (billed at settlement)
IF OBJECT_ID('dbo.MoveOutSettlementLines', 'U') IS NULL
    CREATE TABLE dbo.MoveOutSettlementLines (
        Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MoveOutSettlementLines PRIMARY KEY,
        SettlementId   INT            NOT NULL,
        InvoiceId      INT            NOT NULL,
        LineType       NVARCHAR(20)   NOT NULL,
        BalanceBefore  DECIMAL(18, 2) NOT NULL,
        DepositApplied DECIMAL(18, 2) NOT NULL CONSTRAINT DF_MoveOutSettlementLines_Applied DEFAULT (0),
        CashReceived   DECIMAL(18, 2) NOT NULL CONSTRAINT DF_MoveOutSettlementLines_Cash DEFAULT (0),
        CashPaymentId  INT            NULL,
        CONSTRAINT CK_MoveOutSettlementLines_Type CHECK (LineType IN ('Outstanding', 'Credit', 'Deduction')),
        CONSTRAINT CK_MoveOutSettlementLines_Amounts CHECK (
            DepositApplied >= 0 AND CashReceived >= 0
            AND ((LineType = 'Credit' AND BalanceBefore < 0 AND DepositApplied = 0 AND CashReceived = 0)
                 OR (LineType <> 'Credit' AND BalanceBefore > 0 AND DepositApplied + CashReceived <= BalanceBefore)))
    );
GO
IF OBJECT_ID('dbo.FK_MoveOutSettlementLines_Settlement', 'F') IS NULL
    ALTER TABLE dbo.MoveOutSettlementLines ADD CONSTRAINT FK_MoveOutSettlementLines_Settlement FOREIGN KEY (SettlementId) REFERENCES dbo.MoveOutSettlements (Id);
IF OBJECT_ID('dbo.FK_MoveOutSettlementLines_Invoice', 'F') IS NULL
    ALTER TABLE dbo.MoveOutSettlementLines ADD CONSTRAINT FK_MoveOutSettlementLines_Invoice FOREIGN KEY (InvoiceId) REFERENCES dbo.RentInvoices (Id);
IF OBJECT_ID('dbo.FK_MoveOutSettlementLines_Payment', 'F') IS NULL
    ALTER TABLE dbo.MoveOutSettlementLines ADD CONSTRAINT FK_MoveOutSettlementLines_Payment FOREIGN KEY (CashPaymentId) REFERENCES dbo.Payments (Id);
GO
-- Deposit ledger: link entries to a settlement / invoice, and allow the settlement entry types
IF COL_LENGTH('dbo.SecurityDeposits', 'SettlementId') IS NULL
    ALTER TABLE dbo.SecurityDeposits ADD SettlementId INT NULL;
IF COL_LENGTH('dbo.SecurityDeposits', 'InvoiceId') IS NULL
    ALTER TABLE dbo.SecurityDeposits ADD InvoiceId INT NULL;
GO
IF OBJECT_ID('dbo.FK_SecurityDeposits_Settlement', 'F') IS NULL
    ALTER TABLE dbo.SecurityDeposits ADD CONSTRAINT FK_SecurityDeposits_Settlement FOREIGN KEY (SettlementId) REFERENCES dbo.MoveOutSettlements (Id);
IF OBJECT_ID('dbo.FK_SecurityDeposits_Invoice', 'F') IS NULL
    ALTER TABLE dbo.SecurityDeposits ADD CONSTRAINT FK_SecurityDeposits_Invoice FOREIGN KEY (InvoiceId) REFERENCES dbo.RentInvoices (Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_SecurityDeposits_EntryType' AND definition LIKE '%Refund%')
BEGIN
    ALTER TABLE dbo.SecurityDeposits DROP CONSTRAINT CK_SecurityDeposits_EntryType;
    ALTER TABLE dbo.SecurityDeposits ADD CONSTRAINT CK_SecurityDeposits_EntryType
        CHECK (EntryType IN ('Received', 'Correction', 'Credit Transfer', 'Applied', 'Refund'));
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_SecurityDeposits_Amount' AND definition LIKE '%Refund%')
BEGIN
    ALTER TABLE dbo.SecurityDeposits DROP CONSTRAINT CK_SecurityDeposits_Amount;
    ALTER TABLE dbo.SecurityDeposits ADD CONSTRAINT CK_SecurityDeposits_Amount CHECK (
        (EntryType IN ('Received', 'Credit Transfer') AND Amount > 0 AND Amount <= 1000000000)
        OR (EntryType IN ('Correction', 'Applied', 'Refund') AND Amount < 0 AND Amount >= -1000000000));
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_SecurityDeposits_Method' AND definition LIKE '%Refund%')
BEGIN
    ALTER TABLE dbo.SecurityDeposits DROP CONSTRAINT CK_SecurityDeposits_Method;
    ALTER TABLE dbo.SecurityDeposits ADD CONSTRAINT CK_SecurityDeposits_Method CHECK (
        EntryType NOT IN ('Received', 'Refund')
        OR (PaymentMethod IS NOT NULL AND PaymentMethod IN ('Cash', 'Bank Transfer', 'Cheque', 'Online')));
END
GO
-- Settlement entries must point to their settlement; Applied entries also to the invoice they paid
IF OBJECT_ID('dbo.CK_SecurityDeposits_SettlementLink', 'C') IS NULL
    ALTER TABLE dbo.SecurityDeposits ADD CONSTRAINT CK_SecurityDeposits_SettlementLink CHECK (
        EntryType IN ('Received', 'Correction')
        OR (SettlementId IS NOT NULL AND (EntryType <> 'Applied' OR InvoiceId IS NOT NULL)));
GO
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE Name = N'2026-10-09c_move_out_settlement')
    INSERT INTO dbo.SchemaMigrations (Name) VALUES (N'2026-10-09c_move_out_settlement');
GO
