-- Database rules for what the application already guarantees (audit items C1-C3). No data is changed.
--   Foreign keys  RentInvoices.TenantId -> Tenants, RentInvoices.StatusId -> StatusList, InvoiceAudit.InvoiceId -> RentInvoices
--                 (an existing foreign key on the same column is kept, but made enabled + trusted by re-checking it)
--   NOT NULL      RentInvoices.StatusId (every insert writes 2, the recalculation always sets a status)
--   CHECKs        invoice rent and charged late fee >= 0, invoice late-fee terms >= 0 (settlement deductions use 0),
--                 known charge types (incl. the old "Late Fine" and "Security Deposit"), known payment methods or NULL,
--                 lease rent > 0 and tenure >= 1 month (EndDate is computed from them)
--   Indexes       InvoiceAudit(InvoiceId), RefreshTokens(Token) unique, RefreshTokens(UserId), Payments(TenantId);
--                 IX_RentInvoices_TenantId dropped (IX_RentInvoices_Tenant starts with TenantId and covers it)
-- Not added on purpose, because old rows or the previous API can break them:
--   Payments.PaymentMethod NOT NULL (blank-method discount/waiver rows were stored as NULL until 2026-10-12),
--   a sign rule for payments (old adjustments are negative rows with any method), discount % 0-100 (an old input
--   stored the typed value unchecked), rules on the cached PendingAmount / OverPaidAmount / DiscountAmount columns,
--   DueDate >= InvoiceDate (maintenance billing can differ by a day at midnight).
-- Unchanged: CK_Payments_Reversal, CK_TenantLeases_BilledThrough, vw_UnitOccupancy, RentPayments, InvoiceStatus.
-- ONE batch (no GO) and one transaction: stops with THROW before changing anything if a row breaks a new rule, and
-- records itself in SchemaMigrations inside the same transaction. Safe to re-run: every step checks if it is done.
-- Undo: Database/rollback/2026-10-12c_db_constraints_ROLLBACK.sql.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET LOCK_TIMEOUT 30000;  -- give up (and roll back) instead of waiting behind a long-running request

IF OBJECT_ID('dbo.SchemaMigrations', 'U') IS NULL
   OR NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE Name = N'2026-10-12b_business_date')
    THROW 50003, 'This database is missing earlier migrations (2026-10-12b_business_date). Nothing was changed.', 1;

DECLARE @Bad NVARCHAR(2048) = N'';
SELECT @Bad = CONCAT(@Bad, N' ', v.Msg, N': ', v.N, N'.')
FROM (VALUES
    (N'invoices with an unknown tenant', (SELECT COUNT(*) FROM dbo.RentInvoices ri WHERE NOT EXISTS (SELECT 1 FROM dbo.Tenants t WHERE t.TenantId = ri.TenantId))),
    (N'invoices with no or an unknown status', (SELECT COUNT(*) FROM dbo.RentInvoices ri WHERE NOT EXISTS (SELECT 1 FROM dbo.StatusList s WHERE s.StatusId = ri.StatusId))),
    (N'audit rows of a missing invoice', (SELECT COUNT(*) FROM dbo.InvoiceAudit a WHERE NOT EXISTS (SELECT 1 FROM dbo.RentInvoices ri WHERE ri.Id = a.InvoiceId))),
    (N'invoices with negative rent or charged late fee', (SELECT COUNT(*) FROM dbo.RentInvoices WHERE TotalRent < 0 OR LateFeeCharged < 0)),
    (N'invoices with negative late-fee terms', (SELECT COUNT(*) FROM dbo.RentInvoices WHERE LateFeePerDay < 0 OR LateFeeMaxMultiplier < 0)),
    (N'invoices with an unknown charge type', (SELECT COUNT(*) FROM dbo.RentInvoices
        WHERE ChargeType NOT IN (N'Maintenance', N'Utility', N'Damage', N'Rent Correction', N'Other', N'Cleaning', N'Late Fine', N'Security Deposit'))),
    (N'payments with an unknown method', (SELECT COUNT(*) FROM dbo.Payments
        WHERE PaymentMethod NOT IN ('Cash', 'Bank Transfer', 'Cheque', 'Online', 'Adjustment', 'Security Deposit', 'Credit to Deposit'))),
    (N'leases with rent <= 0 or tenure under 1 month', (SELECT COUNT(*) FROM dbo.TenantLeases WHERE RentAmount <= 0 OR TenureMonths < 1)),
    (N'duplicate refresh tokens', (SELECT COUNT(*) FROM (SELECT Token FROM dbo.RefreshTokens GROUP BY Token HAVING COUNT(*) > 1) d)),
    (N'RentInvoices.StatusId is not INT', (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.RentInvoices') AND name = 'StatusId' AND TYPE_NAME(system_type_id) <> 'int'))
) v (Msg, N)
WHERE v.N > 0;
IF @Bad <> N''
BEGIN
    SET @Bad = CONCAT(N'Nothing was changed. Fix these rows first:', @Bad);
    THROW 50010, @Bad, 1;
END

BEGIN TRAN;

-- RentInvoices.StatusId NOT NULL. Two indexes read StatusId, so the column can't change while they exist: they are
-- dropped here and created again below with the same definition. The transaction keeps RentInvoices schema-locked
-- from the drop to the commit, so no invoice can be written while the one-rent-per-lease-month guard is missing.
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.RentInvoices') AND name = 'StatusId' AND is_nullable = 1)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.RentInvoices') AND name = 'UX_RentInvoices_RentPerLeaseMonth')
        DROP INDEX UX_RentInvoices_RentPerLeaseMonth ON dbo.RentInvoices;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.RentInvoices') AND name = 'IX_RentInvoices_Tenant')
        DROP INDEX IX_RentInvoices_Tenant ON dbo.RentInvoices;
    ALTER TABLE dbo.RentInvoices ALTER COLUMN StatusId INT NOT NULL;
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.RentInvoices') AND name = 'IX_RentInvoices_Tenant')
    CREATE NONCLUSTERED INDEX IX_RentInvoices_Tenant ON dbo.RentInvoices (TenantId, StatusId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.RentInvoices') AND name = 'UX_RentInvoices_RentPerLeaseMonth')
    CREATE UNIQUE NONCLUSTERED INDEX UX_RentInvoices_RentPerLeaseMonth ON dbo.RentInvoices (LeaseId, InvoiceMonth)
        WHERE ChargeType IS NULL AND StatusId <> 6 AND LeaseId IS NOT NULL;

-- Foreign keys. One that already exists on the column (any name) is only counted when it is enabled and trusted;
-- otherwise it is re-checked against all rows (WITH CHECK CHECK CONSTRAINT), which fails if any row breaks it.
DECLARE @Fk TABLE (Tbl SYSNAME, Col SYSNAME, RefTbl SYSNAME, RefCol SYSNAME, NewName SYSNAME);
INSERT @Fk VALUES (N'RentInvoices', N'TenantId', N'Tenants', N'TenantId', N'FK_RentInvoices_Tenant'),
                  (N'RentInvoices', N'StatusId', N'StatusList', N'StatusId', N'FK_RentInvoices_Status'),
                  (N'InvoiceAudit', N'InvoiceId', N'RentInvoices', N'Id', N'FK_InvoiceAudit_Invoice');
DECLARE @FkSql NVARCHAR(MAX) = N'';
SELECT @FkSql = CONCAT(@FkSql,
           CASE WHEN e.name IS NULL
                THEN CONCAT(N'ALTER TABLE dbo.', QUOTENAME(f.Tbl), N' WITH CHECK ADD CONSTRAINT ', QUOTENAME(f.NewName),
                            N' FOREIGN KEY (', QUOTENAME(f.Col), N') REFERENCES dbo.', QUOTENAME(f.RefTbl), N' (', QUOTENAME(f.RefCol), N') ')
                WHEN e.is_disabled = 1 OR e.is_not_trusted = 1
                THEN CONCAT(N'ALTER TABLE dbo.', QUOTENAME(f.Tbl), N' WITH CHECK CHECK CONSTRAINT ', QUOTENAME(e.name), N' ')
                ELSE N'' END)
FROM @Fk f
OUTER APPLY (SELECT TOP (1) k.name, k.is_disabled, k.is_not_trusted
             FROM sys.foreign_keys k JOIN sys.foreign_key_columns c ON c.constraint_object_id = k.object_id
             WHERE k.parent_object_id = OBJECT_ID(N'dbo.' + f.Tbl) AND k.referenced_object_id = OBJECT_ID(N'dbo.' + f.RefTbl)
               AND COL_NAME(c.parent_object_id, c.parent_column_id) = f.Col
               AND COL_NAME(c.referenced_object_id, c.referenced_column_id) = f.RefCol
               AND (SELECT COUNT(*) FROM sys.foreign_key_columns c2 WHERE c2.constraint_object_id = k.object_id) = 1
             ORDER BY k.is_not_trusted, k.is_disabled) e;
IF @FkSql <> N'' EXEC (@FkSql);

-- CHECK rules (same lists as the API: RentService.CashMethods/NoCashMethod/ExtraChargeTypes,
-- SettlementRepository.DepositMethod/CreditMethod, SettlementService.DeductionTypes, plus old frontend charge types)
IF OBJECT_ID('dbo.CK_RentInvoices_Amounts', 'C') IS NULL
    ALTER TABLE dbo.RentInvoices WITH CHECK ADD CONSTRAINT CK_RentInvoices_Amounts CHECK (TotalRent >= 0 AND LateFeeCharged >= 0);
IF OBJECT_ID('dbo.CK_RentInvoices_LateFeeTerms', 'C') IS NULL
    ALTER TABLE dbo.RentInvoices WITH CHECK ADD CONSTRAINT CK_RentInvoices_LateFeeTerms CHECK (LateFeePerDay >= 0 AND LateFeeMaxMultiplier >= 0);
IF OBJECT_ID('dbo.CK_RentInvoices_ChargeType', 'C') IS NULL
    ALTER TABLE dbo.RentInvoices WITH CHECK ADD CONSTRAINT CK_RentInvoices_ChargeType CHECK (ChargeType IS NULL
        OR ChargeType IN (N'Maintenance', N'Utility', N'Damage', N'Rent Correction', N'Other', N'Cleaning', N'Late Fine', N'Security Deposit'));
-- NULL stays allowed (CHECK passes for NULL): old discount/waiver-only rows; the API has not written NULL since 2026-10-12
IF OBJECT_ID('dbo.CK_Payments_Method', 'C') IS NULL
    ALTER TABLE dbo.Payments WITH CHECK ADD CONSTRAINT CK_Payments_Method CHECK (
        PaymentMethod IN ('Cash', 'Bank Transfer', 'Cheque', 'Online', 'Adjustment', 'Security Deposit', 'Credit to Deposit'));
IF OBJECT_ID('dbo.CK_TenantLeases_Terms', 'C') IS NULL
    ALTER TABLE dbo.TenantLeases WITH CHECK ADD CONSTRAINT CK_TenantLeases_Terms CHECK (RentAmount > 0 AND TenureMonths >= 1);
-- on a re-run, a rule someone disabled or added WITH NOCHECK is re-checked and trusted again
DECLARE @CkSql NVARCHAR(MAX) = N'';
SELECT @CkSql = CONCAT(@CkSql, N'ALTER TABLE dbo.', QUOTENAME(OBJECT_NAME(parent_object_id)), N' WITH CHECK CHECK CONSTRAINT ', QUOTENAME(name), N' ')
FROM sys.check_constraints
WHERE name IN (N'CK_RentInvoices_Amounts', N'CK_RentInvoices_LateFeeTerms', N'CK_RentInvoices_ChargeType',
               N'CK_Payments_Method', N'CK_TenantLeases_Terms')
  AND (is_disabled = 1 OR is_not_trusted = 1);
IF @CkSql <> N'' EXEC (@CkSql);

-- Indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.InvoiceAudit') AND name = 'IX_InvoiceAudit_Invoice')
    CREATE NONCLUSTERED INDEX IX_InvoiceAudit_Invoice ON dbo.InvoiceAudit (InvoiceId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.RefreshTokens') AND name = 'UX_RefreshTokens_Token')
    CREATE UNIQUE NONCLUSTERED INDEX UX_RefreshTokens_Token ON dbo.RefreshTokens (Token);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.RefreshTokens') AND name = 'IX_RefreshTokens_User')
    CREATE NONCLUSTERED INDEX IX_RefreshTokens_User ON dbo.RefreshTokens (UserId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Payments') AND name = 'IX_Payments_Tenant')
    CREATE NONCLUSTERED INDEX IX_Payments_Tenant ON dbo.Payments (TenantId);
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.RentInvoices') AND name = 'IX_RentInvoices_TenantId')
   AND EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.RentInvoices') AND name = 'IX_RentInvoices_Tenant')
    DROP INDEX IX_RentInvoices_TenantId ON dbo.RentInvoices;

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE Name = N'2026-10-12c_db_constraints')
    INSERT INTO dbo.SchemaMigrations (Name) VALUES (N'2026-10-12c_db_constraints');
COMMIT;
