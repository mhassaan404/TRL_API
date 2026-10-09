using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.Data;

namespace TRL_API.DAL
{
    // Reports: read only.
    public class ReportRepository : IReportRepository
    {
        private readonly DbHelper _dbHelper;
        public ReportRepository(DbHelper dbHelper) => _dbHelper = dbHelper;

        // Arrears ageing: every open invoice (same statuses and balances as Rent Collection) with its days overdue.
        // Owed = Balance (incl. a charged late fee) + the open late fee not charged yet; a negative balance is a credit.
        // Days overdue use the UTC date, like the late fee in dbo.InvoiceBalance, so both always agree.
        public async Task<DataTable> GetArrearsAgeingAsync() =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                DECLARE @Today DATE = CAST(GETUTCDATE() AS DATE);
                SELECT ri.Id AS InvoiceId, ri.LeaseId, t.TenantId, t.Name AS TenantName, t.Contact AS Phone,
                       bd.BuildingName, f.FloorNumber, u.UnitNumber,
                       ri.InvoiceDate, ri.InvoiceMonth, ri.DueDate, ri.ChargeType,
                       ISNULL(ri.TotalRent, 0) AS Amount,
                       bal.Balance, bal.OpenLateFee AS LateFee,
                       bal.Balance + bal.OpenLateFee AS Owed,
                       CASE WHEN ri.DueDate < @Today THEN DATEDIFF(DAY, ri.DueDate, @Today) ELSE 0 END AS DaysOverdue,
                       @Today AS AsOfDate
                FROM RentInvoices ri
                INNER JOIN Tenants t ON ri.TenantId = t.TenantId
                LEFT JOIN Units u ON u.UnitId = ISNULL(ri.UnitId, t.UnitId)
                LEFT JOIN Floors f ON u.FloorId = f.FloorId
                LEFT JOIN Buildings bd ON f.BuildingId = bd.BuildingId
                CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) bal
                WHERE ri.StatusId IN (2, 8, 9)
                  AND bal.Balance + bal.OpenLateFee <> 0
                ORDER BY t.Name, ri.DueDate, ri.Id;");

        // Collections: every payment received from @From to @To (payment dates, both days included).
        // Amount = money received; Discount = discount given with it. Unit/building come from the invoice.
        public async Task<DataTable> GetCollectionsAsync(DateTime from, DateTime to) =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT p.Id AS PaymentId, p.PaymentDate, p.PaymentAmount AS Amount, p.DiscountAmount AS Discount,
                       p.IsLateFeeWaived AS LateFeeWaived,
                       ISNULL(NULLIF(LTRIM(RTRIM(p.PaymentMethod)), ''), 'Unknown') AS Method,
                       p.Notes, us.Username AS RecordedBy,
                       t.TenantId, t.Name AS TenantName,
                       ri.Id AS InvoiceId, ri.LeaseId, ri.InvoiceMonth, ri.InvoiceDate, ri.ChargeType,
                       bd.BuildingName, f.FloorNumber, u.UnitNumber
                FROM Payments p
                INNER JOIN Tenants t ON p.TenantId = t.TenantId
                LEFT JOIN RentInvoices ri ON ri.Id = p.RentInvoiceId
                LEFT JOIN Units u ON u.UnitId = ISNULL(ri.UnitId, t.UnitId)
                LEFT JOIN Floors f ON u.FloorId = f.FloorId
                LEFT JOIN Buildings bd ON f.BuildingId = bd.BuildingId
                LEFT JOIN Users us ON us.UserId = p.CreatedBy
                WHERE p.PaymentDate >= @From AND p.PaymentDate < DATEADD(DAY, 1, @To)
                  -- Paid from the security deposit / credit moved at a move-out settlement: not money received
                  AND ISNULL(p.PaymentMethod, N'') NOT IN (N'Security Deposit', N'Credit to Deposit')
                ORDER BY p.PaymentDate, p.Id;",
                new[] { new SqlParameter("@From", SqlDbType.Date) { Value = from }, new SqlParameter("@To", SqlDbType.Date) { Value = to } });

        // Tenant picker for statements: current and deleted (former) tenants, so old accounts can still be printed
        public async Task<DataTable> GetStatementTenantsAsync() =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT TenantId, Name AS TenantName, IsDeleted
                FROM Tenants
                ORDER BY IsDeleted, Name;");

        // Tenant details for the statement header. Units = units of active leases, else of any lease, else Tenants.UnitId.
        public async Task<DataTable> GetStatementTenantAsync(int tenantId) =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT t.TenantId, t.Name AS TenantName, t.TenantType, t.ContactPerson, t.Contact, t.Email, t.CnicNtn, t.Address,
                       t.IsDeleted,
                       -- FOR XML PATH instead of STRING_AGG: STRING_AGG needs SQL Server 2017+ (local server is 2016)
                       COALESCE(
                           STUFF((SELECT DISTINCT N', ' + CONCAT(bd.BuildingName + N' – ', u.UnitNumber)
                                  FROM TenantLeases l
                                  INNER JOIN Units u ON u.UnitId = l.UnitId
                                  LEFT JOIN Floors f ON f.FloorId = u.FloorId
                                  LEFT JOIN Buildings bd ON bd.BuildingId = f.BuildingId
                                  WHERE l.TenantId = t.TenantId AND l.IsActive = 1
                                  FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, N''),
                           STUFF((SELECT DISTINCT N', ' + CONCAT(bd.BuildingName + N' – ', u.UnitNumber)
                                  FROM TenantLeases l
                                  INNER JOIN Units u ON u.UnitId = l.UnitId
                                  LEFT JOIN Floors f ON f.FloorId = u.FloorId
                                  LEFT JOIN Buildings bd ON bd.BuildingId = f.BuildingId
                                  WHERE l.TenantId = t.TenantId
                                  FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, N''),
                           (SELECT CONCAT(bd.BuildingName + N' – ', u.UnitNumber)
                            FROM Units u
                            LEFT JOIN Floors f ON f.FloorId = u.FloorId
                            LEFT JOIN Buildings bd ON bd.BuildingId = f.BuildingId
                            WHERE u.UnitId = t.UnitId)) AS Units,
                       ISNULL((SELECT SUM(bal.OpenLateFee)
                               FROM RentInvoices ri CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) bal
                               WHERE ri.TenantId = t.TenantId AND ri.StatusId IN (2, 8, 9)), 0) AS OpenLateFee,
                       CAST(GETUTCDATE() AS DATE) AS AsOfDate
                FROM Tenants t
                WHERE t.TenantId = @TenantId;",
                new[] { new SqlParameter("@TenantId", tenantId) });

        // Every account entry of the tenant up to @To (the service splits it into opening balance and period lines).
        // Charges: invoices (by invoice date) and charged late fees (by charge date).
        // Credits: payments and the discounts given with them (by payment date).
        // Cancelled invoices (StatusId 6) and anything paid against them are left out, like everywhere else,
        // so the balance equals the sum of dbo.InvoiceBalance over the tenant's invoices (minus unlinked payments).
        public async Task<DataTable> GetStatementEntriesAsync(int tenantId, DateTime to) =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT * FROM (
                    SELECT ISNULL(ri.InvoiceDate, CAST(ri.CreatedAt AS DATE)) AS EntryDate, 1 AS SortOrder, N'Invoice' AS EntryType,
                           ri.Id AS InvoiceId, CAST(NULL AS INT) AS PaymentId,
                           CASE WHEN ri.ChargeType IS NOT NULL THEN CONCAT(ri.ChargeType, N' – ' + NULLIF(ri.Description, N''))
                                ELSE CONCAT(N'Rent ', FORMAT(ri.InvoiceMonth, 'MMM yyyy', 'en-US'), N' – ' + NULLIF(ri.Description, N'')) END AS Description,
                           ri.UnitId, CAST(NULL AS NVARCHAR(150)) AS Method, ri.DueDate,
                           ri.TotalRent AS Charge, CAST(0 AS DECIMAL(18, 2)) AS Credit
                    FROM RentInvoices ri
                    WHERE ri.TenantId = @TenantId AND ri.StatusId <> 6

                    UNION ALL
                    SELECT CAST(ISNULL(ri.LateFeeChargedAt, ri.DueDate) AS DATE), 2, N'Late Fee',
                           ri.Id, NULL, CONCAT(N'Late fee on invoice #', ri.Id), ri.UnitId, NULL, NULL,
                           ri.LateFeeCharged, 0
                    FROM RentInvoices ri
                    WHERE ri.TenantId = @TenantId AND ri.StatusId <> 6 AND ri.LateFeeCharged > 0

                    UNION ALL
                    SELECT CAST(p.PaymentDate AS DATE), 3, N'Payment',
                           p.RentInvoiceId, p.Id,
                           CASE WHEN p.RentInvoiceId IS NULL THEN N'Payment' ELSE CONCAT(N'Payment for invoice #', p.RentInvoiceId) END
                             + CASE WHEN p.IsLateFeeWaived = 1 THEN N' (late fee waived)' ELSE N'' END,
                           ri.UnitId, ISNULL(NULLIF(LTRIM(RTRIM(p.PaymentMethod)), ''), 'Unknown'), NULL,
                           0, p.PaymentAmount
                    FROM Payments p
                    LEFT JOIN RentInvoices ri ON ri.Id = p.RentInvoiceId
                    WHERE p.TenantId = @TenantId AND ISNULL(ri.StatusId, 0) <> 6 AND p.PaymentAmount <> 0

                    UNION ALL
                    SELECT CAST(p.PaymentDate AS DATE), 4, N'Discount',
                           p.RentInvoiceId, p.Id,
                           CASE WHEN p.RentInvoiceId IS NULL THEN N'Discount' ELSE CONCAT(N'Discount on invoice #', p.RentInvoiceId) END,
                           ri.UnitId, NULL, NULL,
                           0, p.DiscountAmount
                    FROM Payments p
                    LEFT JOIN RentInvoices ri ON ri.Id = p.RentInvoiceId
                    WHERE p.TenantId = @TenantId AND ISNULL(ri.StatusId, 0) <> 6 AND p.DiscountAmount <> 0
                ) e
                OUTER APPLY (SELECT CONCAT(bd.BuildingName + N' – ', u.UnitNumber) AS Unit
                             FROM Units u
                             LEFT JOIN Floors f ON f.FloorId = u.FloorId
                             LEFT JOIN Buildings bd ON bd.BuildingId = f.BuildingId
                             WHERE u.UnitId = e.UnitId) un
                WHERE e.EntryDate <= @To
                ORDER BY e.EntryDate, e.SortOrder, e.InvoiceId, e.PaymentId;",
                new[] { new SqlParameter("@TenantId", tenantId), new SqlParameter("@To", SqlDbType.Date) { Value = to } });

        // Billing vs Collection: one row per month from @From to @To (first days of months), even months with nothing.
        // Billing side = invoices of that invoice month (cancelled left out) and, as of today, what has been collected
        // against them, discounted, and is still open — so Billed - Discounts - Collected = Outstanding - Credit.
        // CashReceived = payments made in that calendar month, whatever month they paid for.
        // Building (optional) = the invoice's unit (else the tenant's unit), like the other reports.
        public async Task<DataTable> GetBillingVsCollectionAsync(DateTime fromMonth, DateTime toMonth, int? buildingId) =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                ;WITH Months AS (
                    SELECT @From AS MonthStart
                    UNION ALL
                    SELECT DATEADD(MONTH, 1, MonthStart) FROM Months WHERE MonthStart < @To
                ),
                Inv AS (
                    SELECT ri.InvoiceMonth, ri.StatusId, ri.ChargeType, ri.TotalRent, ri.LateFeeCharged,
                           bal.Paid, bal.Disc, bal.Balance, bal.Waived, bal.OpenLateFee
                    FROM RentInvoices ri
                    INNER JOIN Tenants t ON t.TenantId = ri.TenantId
                    LEFT JOIN Units u ON u.UnitId = ISNULL(ri.UnitId, t.UnitId)
                    LEFT JOIN Floors f ON f.FloorId = u.FloorId
                    CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) bal
                    WHERE ri.InvoiceMonth >= @From AND ri.InvoiceMonth <= @To
                      AND (@BuildingId IS NULL OR f.BuildingId = @BuildingId)
                ),
                Cash AS (
                    SELECT DATEFROMPARTS(YEAR(p.PaymentDate), MONTH(p.PaymentDate), 1) AS MonthStart, p.PaymentAmount
                    FROM Payments p
                    INNER JOIN Tenants t ON t.TenantId = p.TenantId
                    LEFT JOIN RentInvoices ri ON ri.Id = p.RentInvoiceId
                    LEFT JOIN Units u ON u.UnitId = ISNULL(ri.UnitId, t.UnitId)
                    LEFT JOIN Floors f ON f.FloorId = u.FloorId
                    WHERE p.PaymentDate >= @From AND p.PaymentDate < DATEADD(MONTH, 1, @To)
                      AND ISNULL(p.PaymentMethod, N'') NOT IN (N'Security Deposit', N'Credit to Deposit') -- not money received
                      AND ISNULL(ri.StatusId, 0) <> 6
                      AND (@BuildingId IS NULL OR f.BuildingId = @BuildingId)
                )
                SELECT m.MonthStart,
                       ISNULL(b.Invoices, 0) AS Invoices,
                       ISNULL(b.RentBilled, 0) AS RentBilled,
                       ISNULL(b.ExtraBilled, 0) AS ExtraBilled,
                       ISNULL(b.LateFeesCharged, 0) AS LateFeesCharged,
                       ISNULL(b.RentBilled, 0) + ISNULL(b.ExtraBilled, 0) + ISNULL(b.LateFeesCharged, 0) AS TotalBilled,
                       ISNULL(b.Discounts, 0) AS Discounts,
                       ISNULL(b.Collected, 0) AS Collected,
                       ISNULL(b.Outstanding, 0) AS Outstanding,
                       ISNULL(b.Credit, 0) AS Credit,
                       ISNULL(b.WaivedInvoices, 0) AS WaivedInvoices,
                       ISNULL(b.AccruingLateFee, 0) AS AccruingLateFee,
                       ISNULL(b.CancelledInvoices, 0) AS CancelledInvoices,
                       ISNULL(b.CancelledAmount, 0) AS CancelledAmount,
                       ISNULL(c.CashReceived, 0) AS CashReceived,
                       CAST(GETUTCDATE() AS DATE) AS AsOfDate
                FROM Months m
                OUTER APPLY (
                    SELECT SUM(CASE WHEN i.StatusId <> 6 THEN 1 ELSE 0 END) AS Invoices,
                           SUM(CASE WHEN i.StatusId <> 6 AND i.ChargeType IS NULL THEN i.TotalRent ELSE 0 END) AS RentBilled,
                           SUM(CASE WHEN i.StatusId <> 6 AND i.ChargeType IS NOT NULL THEN i.TotalRent ELSE 0 END) AS ExtraBilled,
                           SUM(CASE WHEN i.StatusId <> 6 THEN i.LateFeeCharged ELSE 0 END) AS LateFeesCharged,
                           SUM(CASE WHEN i.StatusId <> 6 THEN i.Disc ELSE 0 END) AS Discounts,
                           SUM(CASE WHEN i.StatusId <> 6 THEN i.Paid ELSE 0 END) AS Collected,
                           SUM(CASE WHEN i.StatusId <> 6 AND i.Balance > 0 THEN i.Balance ELSE 0 END) AS Outstanding,
                           SUM(CASE WHEN i.StatusId <> 6 AND i.Balance < 0 THEN -i.Balance ELSE 0 END) AS Credit,
                           SUM(CASE WHEN i.StatusId <> 6 AND i.Waived = 1 THEN 1 ELSE 0 END) AS WaivedInvoices,
                           SUM(CASE WHEN i.StatusId IN (2, 8, 9) THEN i.OpenLateFee ELSE 0 END) AS AccruingLateFee,
                           SUM(CASE WHEN i.StatusId = 6 THEN 1 ELSE 0 END) AS CancelledInvoices,
                           SUM(CASE WHEN i.StatusId = 6 THEN i.TotalRent ELSE 0 END) AS CancelledAmount
                    FROM Inv i WHERE i.InvoiceMonth = m.MonthStart) b
                OUTER APPLY (SELECT SUM(PaymentAmount) AS CashReceived FROM Cash WHERE Cash.MonthStart = m.MonthStart) c
                ORDER BY m.MonthStart
                OPTION (MAXRECURSION 200);",
                new[]
                {
                    new SqlParameter("@From", SqlDbType.Date) { Value = fromMonth },
                    new SqlParameter("@To", SqlDbType.Date) { Value = toMonth },
                    new SqlParameter("@BuildingId", SqlDbType.Int) { Value = (object?)buildingId ?? DBNull.Value },
                });

        // Building filter for reports: every building, including removed ones, so old months can still be filtered
        public async Task<DataTable> GetReportBuildingsAsync() =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT BuildingId, BuildingName, CAST(ISNULL(IsActive, 1) AS BIT) AS IsActive
                FROM Buildings
                ORDER BY ISNULL(IsActive, 1) DESC, BuildingName;");

        // Rent roll: every unit in use (unit, floor and building active, like the Properties page) as of today.
        //  - IsOccupied: same rule as PropertiesRepository (an active lease, or an active tenant still on the unit)
        //  - Current lease = the term in force today: started, and active or renewed with its term still running
        //    (a renewed lease stays the current term until its next term starts, like Lease Management shows it)
        //  - Next lease = an active lease that starts after today (an upcoming renewal or a new booking)
        //  - Arrears = what is owed now on the unit's open invoices (balance + open late fee, like Arrears Ageing)
        // Today = server local date, the same as lease expiry in LeaseRepository.
        public async Task<DataTable> GetRentRollAsync() =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                DECLARE @Today DATE = CAST(GETDATE() AS DATE);
                SELECT u.UnitId, b.BuildingId, b.BuildingName, f.FloorNumber, u.UnitNumber, u.PropertyType, u.BaseRent,
                       us.Name AS MarkedStatus,
                       CAST(CASE WHEN EXISTS (SELECT 1 FROM TenantLeases tl WHERE tl.UnitId = u.UnitId AND tl.IsActive = 1)
                                   OR EXISTS (SELECT 1 FROM Tenants t WHERE t.UnitId = u.UnitId AND t.IsActive = 1 AND t.IsDeleted = 0)
                                 THEN 1 ELSE 0 END AS BIT) AS IsOccupied,
                       cur.LeaseId, cur.TenantId, ISNULL(ct.Name, ft.Name) AS TenantName, ISNULL(ct.Contact, ft.Contact) AS Contact,
                       cur.StartDate, cur.EndDate, cur.TenureMonths, cur.IsActive AS LeaseIsActive,
                       ISNULL(cur.RentAmount, ft.MonthlyRent) AS CurrentRent,
                       nxt.LeaseId AS NextLeaseId, nt.Name AS NextTenantName, nxt.StartDate AS NextStartDate,
                       nxt.EndDate AS NextEndDate, nxt.RentAmount AS NextRent,
                       ISNULL(ar.Arrears, 0) AS Arrears,
                       @Today AS AsOfDate
                FROM Units u
                INNER JOIN Floors f ON f.FloorId = u.FloorId
                INNER JOIN Buildings b ON b.BuildingId = f.BuildingId
                LEFT JOIN UnitStatus us ON us.Id = u.StatusId
                OUTER APPLY (SELECT TOP 1 tl.LeaseId, tl.TenantId, tl.StartDate, tl.EndDate, tl.TenureMonths, tl.IsActive, tl.RentAmount
                             FROM TenantLeases tl
                             WHERE tl.UnitId = u.UnitId AND tl.StartDate <= @Today
                               AND (tl.IsActive = 1 OR (tl.TerminationReason = 'Renewed' AND tl.BilledThrough >= @Today))
                             ORDER BY tl.IsActive DESC, tl.StartDate DESC) cur
                LEFT JOIN Tenants ct ON ct.TenantId = cur.TenantId
                -- No lease: an active tenant still pointing at the unit (old data before leases)
                OUTER APPLY (SELECT TOP 1 t.Name, t.Contact, t.MonthlyRent
                             FROM Tenants t
                             WHERE cur.LeaseId IS NULL AND t.UnitId = u.UnitId AND t.IsActive = 1 AND t.IsDeleted = 0
                             ORDER BY t.TenantId DESC) ft
                OUTER APPLY (SELECT TOP 1 tl.LeaseId, tl.TenantId, tl.StartDate, tl.EndDate, tl.RentAmount
                             FROM TenantLeases tl
                             WHERE tl.UnitId = u.UnitId AND tl.IsActive = 1 AND tl.StartDate > @Today
                             ORDER BY tl.StartDate) nxt
                LEFT JOIN Tenants nt ON nt.TenantId = nxt.TenantId
                OUTER APPLY (SELECT SUM(CASE WHEN bal.Balance + bal.OpenLateFee > 0 THEN bal.Balance + bal.OpenLateFee ELSE 0 END) AS Arrears
                             FROM RentInvoices ri
                             CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) bal
                             WHERE ri.UnitId = u.UnitId AND ri.StatusId IN (2, 8, 9)) ar
                WHERE u.IsActive = 1 AND f.IsActive = 1 AND b.IsActive = 1
                ORDER BY b.BuildingName, f.FloorNumber, u.UnitNumber;");

        // Maintenance cost: every job whose job date is in @From..@To. Job date = completed date, else reported date
        // (so finished work counts in the month it was done, open work in the month it was reported).
        // Billed = the job's charge invoice when it is not cancelled (like MaintenanceRepository.IsBilled);
        // Recovered = what the tenant has paid on that invoice so far.
        public async Task<DataTable> GetMaintenanceCostAsync(DateTime from, DateTime to) =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT j.Id AS JobId, j.Title, j.Category, j.Priority, j.Status, j.AssignedTo,
                       j.ReportedDate, j.CompletedDate, ISNULL(j.CompletedDate, j.ReportedDate) AS JobDate,
                       CASE WHEN j.CompletedDate IS NOT NULL THEN DATEDIFF(DAY, j.ReportedDate, j.CompletedDate) END AS DaysToComplete,
                       j.Cost, b.BuildingName, f.FloorNumber, u.UnitNumber, t.Name AS TenantName,
                       CASE WHEN ri.Id IS NOT NULL AND ri.StatusId <> 6 THEN ri.Id END AS ChargeInvoiceId,
                       CASE WHEN ri.Id IS NOT NULL AND ri.StatusId <> 6 THEN ri.TotalRent ELSE 0 END AS Billed,
                       CASE WHEN ri.Id IS NOT NULL AND ri.StatusId <> 6 THEN bal.Paid ELSE 0 END AS Recovered
                FROM MaintenanceJobs j
                INNER JOIN Buildings b ON b.BuildingId = j.BuildingId
                LEFT JOIN Floors f ON f.FloorId = j.FloorId
                LEFT JOIN Units u ON u.UnitId = j.UnitId
                LEFT JOIN Tenants t ON t.TenantId = j.TenantId
                LEFT JOIN RentInvoices ri ON ri.Id = j.ChargeInvoiceId
                OUTER APPLY dbo.InvoiceBalance(ri.Id, 0) bal
                WHERE ISNULL(j.CompletedDate, j.ReportedDate) >= @From
                  AND ISNULL(j.CompletedDate, j.ReportedDate) <= @To
                ORDER BY JobDate, j.Id;",
                new[] { new SqlParameter("@From", SqlDbType.Date) { Value = from }, new SqlParameter("@To", SqlDbType.Date) { Value = to } });
    }
}
