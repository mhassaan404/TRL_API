-- Payments.PaymentAmount decimal(10,2) -> decimal(18,2), like every other money column. A payment above about
-- PKR 99.9 million (possible at a move-out settlement, which allows up to 1,000,000,000) could not be stored.
-- Widening keeps every value exactly. CK_Payments_Reversal reads the column, so it is dropped and created again
-- (same definition) around the change, all in one transaction. Safe to re-run: does nothing once the column is (18,2).
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Payments') AND name = 'PaymentAmount' AND precision < 18)
BEGIN
    BEGIN TRAN;
    IF OBJECT_ID('dbo.CK_Payments_Reversal', 'C') IS NOT NULL
        ALTER TABLE dbo.Payments DROP CONSTRAINT CK_Payments_Reversal;
    ALTER TABLE dbo.Payments ALTER COLUMN PaymentAmount DECIMAL(18, 2) NOT NULL;
    ALTER TABLE dbo.Payments ADD CONSTRAINT CK_Payments_Reversal CHECK (
        ReversalOfPaymentId IS NULL
        OR (ReversalOfPaymentId <> Id AND PaymentAmount <= 0 AND DiscountAmount <= 0 AND IsLateFeeWaived = 0));
    COMMIT;
END
GO
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE Name = N'2026-10-12_payment_amount_precision')
    INSERT INTO dbo.SchemaMigrations (Name) VALUES (N'2026-10-12_payment_amount_precision');
GO
