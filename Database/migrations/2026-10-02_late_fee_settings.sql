-- Late fee settings, edited by Admin on the Late Fee Settings page.
--   dbo.LateFeeSettings (one row, Id = 1):
--     PaymentDueDays        due date = invoice date + this many days (used when invoices are generated)
--     LateFeePerDay         late fee for each day an invoice is unpaid after its due date
--     MaxLateFeeMultiplier  the late fee stops growing at this multiple of the invoice's rent amount
--   Seeded with the rules that were hard-coded until now: 5 days, 500 per day, 2 x invoice rent.
--
-- Each invoice keeps the late-fee rule it was created with (RentInvoices.LateFeePerDay / LateFeeMaxMultiplier),
-- so changing the settings never re-prices existing invoices: due dates are already stored per invoice, and
-- the rate and cap now are too. Existing invoices are filled with the old fixed rule (500 / 2), so their late
-- fees stay exactly as before.
--
-- dbo.CalculateLateFee takes the rate and cap as parameters instead of the fixed 500 and 2;
-- dbo.InvoiceBalance passes the invoice's own values. The calculation is otherwise unchanged.
-- Safe to re-run.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
BEGIN TRAN;

IF OBJECT_ID('dbo.LateFeeSettings', 'U') IS NULL
    CREATE TABLE dbo.LateFeeSettings
    (
        Id INT NOT NULL CONSTRAINT PK_LateFeeSettings PRIMARY KEY,
        PaymentDueDays INT NOT NULL,
        LateFeePerDay DECIMAL(18, 2) NOT NULL,
        MaxLateFeeMultiplier DECIMAL(5, 2) NOT NULL,
        UpdatedBy INT NULL,
        UpdatedAt DATETIME NULL,
        CONSTRAINT CK_LateFeeSettings_SingleRow CHECK (Id = 1),
        CONSTRAINT CK_LateFeeSettings_PaymentDueDays CHECK (PaymentDueDays BETWEEN 0 AND 90),
        CONSTRAINT CK_LateFeeSettings_LateFeePerDay CHECK (LateFeePerDay > 0 AND LateFeePerDay <= 100000),
        CONSTRAINT CK_LateFeeSettings_MaxLateFeeMultiplier CHECK (MaxLateFeeMultiplier > 0 AND MaxLateFeeMultiplier <= 12)
    );

IF NOT EXISTS (SELECT 1 FROM dbo.LateFeeSettings WHERE Id = 1)
    INSERT INTO dbo.LateFeeSettings (Id, PaymentDueDays, LateFeePerDay, MaxLateFeeMultiplier) VALUES (1, 5, 500, 2);

IF COL_LENGTH('dbo.RentInvoices', 'LateFeePerDay') IS NULL
    ALTER TABLE dbo.RentInvoices ADD LateFeePerDay DECIMAL(18, 2) NULL;

IF COL_LENGTH('dbo.RentInvoices', 'LateFeeMaxMultiplier') IS NULL
    ALTER TABLE dbo.RentInvoices ADD LateFeeMaxMultiplier DECIMAL(5, 2) NULL;

COMMIT;
GO

-- Separate batch: the columns must exist first. Existing invoices keep the rule they were created under.
UPDATE dbo.RentInvoices SET LateFeePerDay = 500 WHERE LateFeePerDay IS NULL;
UPDATE dbo.RentInvoices SET LateFeeMaxMultiplier = 2 WHERE LateFeeMaxMultiplier IS NULL;
GO

-- Every new invoice must carry its rule (no default, so no code path can skip it)
ALTER TABLE dbo.RentInvoices ALTER COLUMN LateFeePerDay DECIMAL(18, 2) NOT NULL;
ALTER TABLE dbo.RentInvoices ALTER COLUMN LateFeeMaxMultiplier DECIMAL(5, 2) NOT NULL;
GO

-- Both functions change together: InvoiceBalance calls CalculateLateFee
BEGIN TRAN;
GO

CREATE OR ALTER FUNCTION dbo.CalculateLateFee
(
    @RemainingAmount DECIMAL(18,2),
    @MonthlyRent DECIMAL(18,2),
    @DueDate DATE,
    @CurrentDate DATE,
    @LateFeePerDay DECIMAL(18,2),
    @MaxMultiplier DECIMAL(5,2)
)
RETURNS DECIMAL(18,2)
AS
BEGIN
    DECLARE @DaysOverdue INT = DATEDIFF(DAY, @DueDate, @CurrentDate);

    -- Not overdue yet (on or before the due date): no late fee
    IF @DaysOverdue <= 0
        RETURN 0;

    -- A flat fee for each day overdue, capped at a multiple of the invoice's rent
    DECLARE @LateFee DECIMAL(18,2) = @LateFeePerDay * @DaysOverdue;

    IF @LateFee > (@MonthlyRent * @MaxMultiplier)
        SET @LateFee = @MonthlyRent * @MaxMultiplier;

    RETURN ISNULL(@LateFee, 0);
END
GO

CREATE OR ALTER FUNCTION dbo.InvoiceBalance (@InvoiceId INT, @ExcludePaymentId INT)
RETURNS TABLE
AS
RETURN
    SELECT ri.Id AS InvoiceId, p.Paid, p.Disc, p.Waived, p.LastPaymentDate,
           ri.TotalRent - p.Paid - p.Disc AS RentBalance,
           ri.TotalRent + ri.LateFeeCharged - p.Paid - p.Disc AS Balance,
           CAST(CASE WHEN ri.LateFeeCharged = 0 AND p.Waived = 0
                          AND ri.DueDate < CAST(GETUTCDATE() AS DATE)
                          AND ri.TotalRent - p.Paid - p.Disc > 0
                     THEN dbo.CalculateLateFee(ri.TotalRent - p.Paid - p.Disc, ri.TotalRent, ri.DueDate, GETUTCDATE(),
                                               ri.LateFeePerDay, ri.LateFeeMaxMultiplier)
                     ELSE 0 END AS DECIMAL(18, 2)) AS OpenLateFee
    FROM dbo.RentInvoices ri
    OUTER APPLY (SELECT ISNULL(SUM(PaymentAmount), 0) AS Paid,
                        ISNULL(SUM(DiscountAmount), 0) AS Disc,
                        ISNULL(MAX(CASE WHEN IsLateFeeWaived = 1 THEN 1 ELSE 0 END), 0) AS Waived,
                        MAX(PaymentDate) AS LastPaymentDate
                 FROM dbo.Payments
                 WHERE RentInvoiceId = ri.Id AND Id <> @ExcludePaymentId) p
    WHERE ri.Id = @InvoiceId;
GO

COMMIT;
GO
