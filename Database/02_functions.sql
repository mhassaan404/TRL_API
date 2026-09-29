-- TRL_DB: user-defined functions
-- Generated from VICTUS15\SQLEXPRESS (SQL Server 2016). Schema only, no data.

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE FUNCTION [dbo].[CalculateLateFee]
(
    @RemainingAmount DECIMAL(18,2),
    @MonthlyRent DECIMAL(18,2),
    @DueDate DATE,
    @CurrentDate DATE
)
RETURNS DECIMAL(18,2)
AS
BEGIN
    DECLARE @DaysOverdue INT = DATEDIFF(DAY, @DueDate, @CurrentDate);
    
    -- If not overdue or negative days → return 0
    IF @DaysOverdue <= 0
        RETURN 0;
    
    -- Example logic - adjust to your exact business rules
    -- Option 1: Flat fee per day (common in Pakistan rentals)
    DECLARE @DailyLateFee DECIMAL(18,2) = 500.00;  -- e.g., 500 PKR per day
    
    -- Option 2: Percentage of remaining rent per day
    -- DECLARE @DailyLateFee DECIMAL(18,2) = @RemainingAmount * 0.02;  -- 2% per day
    
    -- Option 3: Percentage of monthly rent per month overdue
    -- DECLARE @MonthlyPenalty DECIMAL(18,2) = @MonthlyRent * 0.10;  -- 10% of monthly rent
    -- DECLARE @LateFee DECIMAL(18,2) = @MonthlyPenalty * (@DaysOverdue / 30.0);
    
    -- Simple flat daily example (change as per your rules)
    DECLARE @LateFee DECIMAL(18,2) = @DailyLateFee * @DaysOverdue;
    
    -- Optional: Cap the maximum late fee
    IF @LateFee > (@MonthlyRent * 2)  -- e.g., max 2 months rent
        SET @LateFee = @MonthlyRent * 2;
    
    RETURN ISNULL(@LateFee, 0);
END
GO

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   FUNCTION dbo.InvoiceBalance (@InvoiceId INT, @ExcludePaymentId INT)
RETURNS TABLE
AS
RETURN
    SELECT ri.Id AS InvoiceId, p.Paid, p.Disc, p.Waived, p.LastPaymentDate,
           ri.TotalRent - p.Paid - p.Disc AS RentBalance,
           ri.TotalRent + ri.LateFeeCharged - p.Paid - p.Disc AS Balance,
           CAST(CASE WHEN ri.LateFeeCharged = 0 AND p.Waived = 0
                          AND ri.DueDate < CAST(GETUTCDATE() AS DATE)
                          AND ri.TotalRent - p.Paid - p.Disc > 0
                     THEN dbo.CalculateLateFee(ri.TotalRent - p.Paid - p.Disc, ri.TotalRent, ri.DueDate, GETUTCDATE())
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

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- Covered days and rent of a lease in one month (@MonthStart = first day of the month).
-- Full month = the lease rent; partial month = rent x covered days / days in month, rounded to whole rupees.
CREATE   FUNCTION dbo.LeaseMonthCharge (@LeaseId INT, @MonthStart DATE)
RETURNS TABLE
AS
RETURN
    SELECT l.LeaseId, x.FromDate, x.ToDate, x.DaysInMonth,
           CASE WHEN x.Days > 0 THEN x.Days ELSE 0 END AS Days,
           CAST(CASE WHEN x.Days <= 0 THEN 0
                     WHEN x.Days = x.DaysInMonth THEN l.RentAmount
                     ELSE ROUND(l.RentAmount * x.Days / x.DaysInMonth, 0) END AS DECIMAL(18, 2)) AS Amount,
           CASE WHEN x.Days > 0 AND x.Days < x.DaysInMonth
                THEN CONCAT(N'Rent ', FORMAT(x.FromDate, 'dd MMM'), N' - ', FORMAT(x.ToDate, 'dd MMM'),
                            N' (', x.Days, N' of ', x.DaysInMonth, N' days)') END AS Descr
    FROM dbo.TenantLeases l
    CROSS APPLY (SELECT CASE WHEN l.StartDate > @MonthStart THEN l.StartDate ELSE @MonthStart END AS FromDate,
                        CASE WHEN l.BilledThrough < EOMONTH(@MonthStart) THEN l.BilledThrough ELSE EOMONTH(@MonthStart) END AS ToDate,
                        DAY(EOMONTH(@MonthStart)) AS DaysInMonth) d
    CROSS APPLY (SELECT d.FromDate, d.ToDate, d.DaysInMonth, DATEDIFF(DAY, d.FromDate, d.ToDate) + 1 AS Days) x
    WHERE l.LeaseId = @LeaseId;
GO

