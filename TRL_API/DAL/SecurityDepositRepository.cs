using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.Data;
using TRL_API.Models;

namespace TRL_API.DAL
{
    // Security deposits: money held for a tenancy (tenant + unit). Never touches RentInvoices or Payments.
    // A tenancy's held balance = the sum of its SecurityDeposits entries (Received > 0, Correction < 0).
    public class SecurityDepositRepository : ISecurityDepositRepository
    {
        private readonly DbHelper _dbHelper;
        public SecurityDepositRepository(DbHelper dbHelper) => _dbHelper = dbHelper;

        // A lease counts as current when it is active (running or starting later) or renewed with its term still
        // running (same rule as Lease Management). Today = server date, like the lease pages.
        private const string CurrentLeaseSql =
            "(l.IsActive = 1 OR (l.TerminationReason = 'Renewed' AND l.BilledThrough >= CAST(GETDATE() AS DATE)))";

        // One row per tenancy that has a current lease, or whose lease has ended while a deposit is still held.
        // LeaseId = the lease in force today (else the one starting next); ended tenancies show their last lease.
        public async Task<DataTable> GetTenanciesAsync() =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync($@"
                DECLARE @Today DATE = CAST(GETDATE() AS DATE);
                ;WITH Cur AS (
                    SELECT l.TenantId, l.UnitId, l.LeaseId, l.StartDate, l.EndDate, l.RentAmount,
                           ROW_NUMBER() OVER (PARTITION BY l.TenantId, l.UnitId
                                              ORDER BY CASE WHEN l.StartDate <= @Today THEN 0 ELSE 1 END,
                                                       CASE WHEN l.StartDate <= @Today THEN l.StartDate END DESC, l.StartDate) AS rn
                    FROM TenantLeases l
                    WHERE {CurrentLeaseSql}
                ),
                Bal AS (
                    SELECT TenantId, UnitId, SUM(Amount) AS Held,
                           SUM(CASE WHEN EntryType = 'Received' THEN Amount ELSE 0 END) AS TotalReceived,
                           MAX(CASE WHEN EntryType = 'Received' THEN EntryDate END) AS LastReceivedDate,
                           COUNT(*) AS Entries
                    FROM SecurityDeposits
                    GROUP BY TenantId, UnitId
                ),
                Tenancy AS (
                    SELECT TenantId, UnitId FROM Cur WHERE rn = 1
                    UNION
                    SELECT TenantId, UnitId FROM Bal WHERE Held <> 0
                )
                SELECT x.TenantId, x.UnitId, t.Name AS TenantName, t.Contact, t.IsDeleted AS TenantDeleted,
                       b.BuildingName, f.FloorNumber, u.UnitNumber,
                       CAST(CASE WHEN c.LeaseId IS NULL THEN 0 ELSE 1 END AS BIT) AS HasCurrentLease,
                       COALESCE(c.LeaseId, last.LeaseId) AS LeaseId,
                       COALESCE(c.StartDate, last.StartDate) AS LeaseStartDate,
                       COALESCE(c.EndDate, last.EndDate) AS LeaseEndDate,
                       COALESCE(c.RentAmount, last.RentAmount) AS MonthlyRent,
                       CAST(CASE WHEN c.StartDate > @Today THEN 1 ELSE 0 END AS BIT) AS LeaseUpcoming,
                       dt.AgreedAmount,
                       ISNULL(bal.Held, 0) AS Held, ISNULL(bal.TotalReceived, 0) AS TotalReceived,
                       bal.LastReceivedDate, ISNULL(bal.Entries, 0) AS Entries
                FROM Tenancy x
                INNER JOIN Tenants t ON t.TenantId = x.TenantId
                INNER JOIN Units u ON u.UnitId = x.UnitId
                LEFT JOIN Floors f ON f.FloorId = u.FloorId
                LEFT JOIN Buildings b ON b.BuildingId = f.BuildingId
                LEFT JOIN Cur c ON c.TenantId = x.TenantId AND c.UnitId = x.UnitId AND c.rn = 1
                OUTER APPLY (SELECT TOP 1 l.LeaseId, l.StartDate, l.EndDate, l.RentAmount FROM TenantLeases l
                             WHERE l.TenantId = x.TenantId AND l.UnitId = x.UnitId ORDER BY l.StartDate DESC, l.LeaseId DESC) last
                LEFT JOIN SecurityDepositTerms dt ON dt.TenantId = x.TenantId AND dt.UnitId = x.UnitId
                LEFT JOIN Bal bal ON bal.TenantId = x.TenantId AND bal.UnitId = x.UnitId
                ORDER BY t.Name, b.BuildingName, u.UnitNumber;");

        // Every entry of one tenancy, oldest first, with the held balance after each entry
        public async Task<DataTable> GetHistoryAsync(int tenantId, int unitId) =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT d.Id AS DepositId, d.EntryType, d.Amount, d.EntryDate, d.PaymentMethod, d.Reference, d.Notes,
                       d.LeaseId, d.CreatedAt, u.Username AS RecordedBy,
                       SUM(d.Amount) OVER (ORDER BY d.EntryDate, d.Id ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS HeldAfter
                FROM SecurityDeposits d
                LEFT JOIN Users u ON u.UserId = d.CreatedBy
                WHERE d.TenantId = @TenantId AND d.UnitId = @UnitId
                ORDER BY d.EntryDate, d.Id;",
                new[] { new SqlParameter("@TenantId", tenantId), new SqlParameter("@UnitId", unitId) });

        // Result codes: OK (Info = new entry Id), NOT_FOUND, NOT_CURRENT, TENANT_DELETED, DUPLICATE, OVER_AGREED (Info = the
        // amount still due), BUSY. A tenancy lock serialises entries of the same tenancy, so the agreed-amount check
        // can't be raced.
        public async Task<(string Result, decimal? Info)> RecordAsync(RecordDepositRequest r, int userId)
        {
            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync($@"
                SET XACT_ABORT ON;
                DECLARE @TenantId INT, @UnitId INT, @Current BIT, @Deleted BIT;
                SELECT @TenantId = l.TenantId, @UnitId = l.UnitId,
                       @Current = CASE WHEN {CurrentLeaseSql} THEN 1 ELSE 0 END, @Deleted = t.IsDeleted
                FROM TenantLeases l INNER JOIN Tenants t ON t.TenantId = l.TenantId
                WHERE l.LeaseId = @LeaseId;
                IF @TenantId IS NULL BEGIN SELECT 'NOT_FOUND' AS Result, CAST(NULL AS DECIMAL(18, 2)) AS Info; RETURN; END
                IF @Deleted = 1 BEGIN SELECT 'TENANT_DELETED' AS Result, CAST(NULL AS DECIMAL(18, 2)) AS Info; RETURN; END
                IF @Current = 0 BEGIN SELECT 'NOT_CURRENT' AS Result, CAST(NULL AS DECIMAL(18, 2)) AS Info; RETURN; END

                BEGIN TRAN;
                DECLARE @Lock INT, @Resource NVARCHAR(100) = CONCAT(N'TRL_Deposit_', @TenantId, N'_', @UnitId);
                EXEC @Lock = sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
                IF @Lock < 0 BEGIN ROLLBACK; SELECT 'BUSY' AS Result, CAST(NULL AS DECIMAL(18, 2)) AS Info; RETURN; END

                -- The same deposit submitted twice (double click / retry) within a minute
                IF EXISTS (SELECT 1 FROM SecurityDeposits
                           WHERE TenantId = @TenantId AND UnitId = @UnitId AND EntryType = 'Received' AND Amount = @Amount
                             AND EntryDate = @EntryDate AND PaymentMethod = @Method AND ISNULL(Reference, N'') = ISNULL(@Reference, N'')
                             AND CreatedAt >= DATEADD(SECOND, -60, GETDATE()))
                BEGIN ROLLBACK; SELECT 'DUPLICATE' AS Result, CAST(NULL AS DECIMAL(18, 2)) AS Info; RETURN; END

                DECLARE @Agreed DECIMAL(18, 2) = (SELECT AgreedAmount FROM SecurityDepositTerms WHERE TenantId = @TenantId AND UnitId = @UnitId);
                DECLARE @Held DECIMAL(18, 2) = ISNULL((SELECT SUM(Amount) FROM SecurityDeposits WHERE TenantId = @TenantId AND UnitId = @UnitId), 0);
                IF @Agreed IS NOT NULL AND @Held + @Amount > @Agreed
                BEGIN ROLLBACK; SELECT 'OVER_AGREED' AS Result, CASE WHEN @Agreed > @Held THEN @Agreed - @Held ELSE 0 END AS Info; RETURN; END

                INSERT INTO SecurityDeposits (TenantId, UnitId, LeaseId, EntryType, Amount, EntryDate, PaymentMethod, Reference, Notes, CreatedBy)
                VALUES (@TenantId, @UnitId, @LeaseId, 'Received', @Amount, @EntryDate, @Method, @Reference, @Notes, @UserId);
                DECLARE @Id INT = SCOPE_IDENTITY();
                COMMIT;
                SELECT 'OK' AS Result, CAST(@Id AS DECIMAL(18, 2)) AS Info;",
                new[]
                {
                    new SqlParameter("@LeaseId", r.LeaseId),
                    Money("@Amount", r.Amount!.Value),
                    new SqlParameter("@EntryDate", SqlDbType.Date) { Value = r.EntryDate!.Value.Date },
                    new SqlParameter("@Method", SqlDbType.NVarChar, 30) { Value = r.PaymentMethod! },
                    Text("@Reference", r.Reference, 100),
                    Text("@Notes", r.Notes, 500),
                    new SqlParameter("@UserId", userId),
                });
            return Result(dt);
        }

        // Result codes: OK (Info = new entry Id), NOT_FOUND (no lease for this tenancy), OVER_HELD (Info = held), DUPLICATE, BUSY
        public async Task<(string Result, decimal? Info)> CorrectAsync(CorrectDepositRequest r, int userId)
        {
            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SET XACT_ABORT ON;
                DECLARE @LeaseId INT = (SELECT TOP 1 LeaseId FROM TenantLeases WHERE TenantId = @TenantId AND UnitId = @UnitId
                                        ORDER BY StartDate DESC, LeaseId DESC);
                IF @LeaseId IS NULL BEGIN SELECT 'NOT_FOUND' AS Result, CAST(NULL AS DECIMAL(18, 2)) AS Info; RETURN; END

                BEGIN TRAN;
                DECLARE @Lock INT, @Resource NVARCHAR(100) = CONCAT(N'TRL_Deposit_', @TenantId, N'_', @UnitId);
                EXEC @Lock = sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
                IF @Lock < 0 BEGIN ROLLBACK; SELECT 'BUSY' AS Result, CAST(NULL AS DECIMAL(18, 2)) AS Info; RETURN; END

                IF EXISTS (SELECT 1 FROM SecurityDeposits
                           WHERE TenantId = @TenantId AND UnitId = @UnitId AND EntryType = 'Correction' AND Amount = -@Amount
                             AND EntryDate = @EntryDate AND Notes = @Reason AND CreatedAt >= DATEADD(SECOND, -60, GETDATE()))
                BEGIN ROLLBACK; SELECT 'DUPLICATE' AS Result, CAST(NULL AS DECIMAL(18, 2)) AS Info; RETURN; END

                DECLARE @Held DECIMAL(18, 2) = ISNULL((SELECT SUM(Amount) FROM SecurityDeposits WHERE TenantId = @TenantId AND UnitId = @UnitId), 0);
                IF @Amount > @Held BEGIN ROLLBACK; SELECT 'OVER_HELD' AS Result, @Held AS Info; RETURN; END

                INSERT INTO SecurityDeposits (TenantId, UnitId, LeaseId, EntryType, Amount, EntryDate, Notes, CreatedBy)
                VALUES (@TenantId, @UnitId, @LeaseId, 'Correction', -@Amount, @EntryDate, @Reason, @UserId);
                DECLARE @Id INT = SCOPE_IDENTITY();
                COMMIT;
                SELECT 'OK' AS Result, CAST(@Id AS DECIMAL(18, 2)) AS Info;",
                new[]
                {
                    new SqlParameter("@TenantId", r.TenantId),
                    new SqlParameter("@UnitId", r.UnitId),
                    Money("@Amount", r.Amount!.Value),
                    new SqlParameter("@EntryDate", SqlDbType.Date) { Value = r.EntryDate!.Value.Date },
                    Text("@Reason", r.Reason, 500),
                    new SqlParameter("@UserId", userId),
                });
            return Result(dt);
        }

        // Sets (or clears, when AgreedAmount is null) the agreed deposit of a tenancy.
        // Result codes: OK, NOT_FOUND (no lease for this tenant + unit), BELOW_HELD (Info = held), BUSY
        public async Task<(string Result, decimal? Info)> SetAgreedAsync(SetDepositAgreedRequest r, int userId)
        {
            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SET XACT_ABORT ON;
                IF NOT EXISTS (SELECT 1 FROM TenantLeases WHERE TenantId = @TenantId AND UnitId = @UnitId)
                BEGIN SELECT 'NOT_FOUND' AS Result, CAST(NULL AS DECIMAL(18, 2)) AS Info; RETURN; END

                BEGIN TRAN;
                DECLARE @Lock INT, @Resource NVARCHAR(100) = CONCAT(N'TRL_Deposit_', @TenantId, N'_', @UnitId);
                EXEC @Lock = sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
                IF @Lock < 0 BEGIN ROLLBACK; SELECT 'BUSY' AS Result, CAST(NULL AS DECIMAL(18, 2)) AS Info; RETURN; END

                DECLARE @Held DECIMAL(18, 2) = ISNULL((SELECT SUM(Amount) FROM SecurityDeposits WHERE TenantId = @TenantId AND UnitId = @UnitId), 0);
                IF @Agreed IS NOT NULL AND @Agreed < @Held BEGIN ROLLBACK; SELECT 'BELOW_HELD' AS Result, @Held AS Info; RETURN; END

                IF @Agreed IS NULL
                    DELETE FROM SecurityDepositTerms WHERE TenantId = @TenantId AND UnitId = @UnitId;
                ELSE IF EXISTS (SELECT 1 FROM SecurityDepositTerms WHERE TenantId = @TenantId AND UnitId = @UnitId)
                    UPDATE SecurityDepositTerms SET AgreedAmount = @Agreed, UpdatedBy = @UserId, UpdatedAt = GETDATE()
                    WHERE TenantId = @TenantId AND UnitId = @UnitId;
                ELSE
                    INSERT INTO SecurityDepositTerms (TenantId, UnitId, AgreedAmount, UpdatedBy) VALUES (@TenantId, @UnitId, @Agreed, @UserId);
                COMMIT;
                SELECT 'OK' AS Result, CAST(NULL AS DECIMAL(18, 2)) AS Info;",
                new[]
                {
                    new SqlParameter("@TenantId", r.TenantId),
                    new SqlParameter("@UnitId", r.UnitId),
                    new SqlParameter("@Agreed", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = (object?)r.AgreedAmount ?? DBNull.Value },
                    new SqlParameter("@UserId", userId),
                });
            return Result(dt);
        }

        // A Received entry with tenant, unit (building address) and the tenancy's held balance after it, for printing
        public async Task<DataTable> GetReceiptAsync(int depositId) =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT d.Id AS DepositId, d.EntryType, d.Amount, d.EntryDate, d.PaymentMethod, d.Reference, d.Notes, d.LeaseId,
                       u2.Username AS RecordedBy,
                       t.TenantId, t.Name AS TenantName, t.TenantType, t.ContactPerson, t.Contact, t.Email, t.CnicNtn, t.Address AS TenantAddress,
                       b.BuildingName, b.Address AS BuildingAddress, f.FloorNumber, u.UnitNumber,
                       l.StartDate AS LeaseStartDate, l.EndDate AS LeaseEndDate,
                       dt.AgreedAmount,
                       (SELECT SUM(x.Amount) FROM SecurityDeposits x WHERE x.TenantId = d.TenantId AND x.UnitId = d.UnitId
                          AND (x.EntryDate < d.EntryDate OR (x.EntryDate = d.EntryDate AND x.Id <= d.Id))) AS HeldAfter
                FROM SecurityDeposits d
                INNER JOIN Tenants t ON t.TenantId = d.TenantId
                INNER JOIN Units u ON u.UnitId = d.UnitId
                LEFT JOIN Floors f ON f.FloorId = u.FloorId
                LEFT JOIN Buildings b ON b.BuildingId = f.BuildingId
                LEFT JOIN TenantLeases l ON l.LeaseId = d.LeaseId
                LEFT JOIN SecurityDepositTerms dt ON dt.TenantId = d.TenantId AND dt.UnitId = d.UnitId
                LEFT JOIN Users u2 ON u2.UserId = d.CreatedBy
                WHERE d.Id = @Id;",
                new[] { new SqlParameter("@Id", depositId) });

        private static (string, decimal?) Result(DataTable dt)
        {
            var row = dt.Rows[0];
            return (row["Result"].ToString()!, row["Info"] == DBNull.Value ? null : Convert.ToDecimal(row["Info"]));
        }

        private static SqlParameter Money(string name, decimal value) =>
            new(name, SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = value };

        private static SqlParameter Text(string name, string? value, int size) =>
            new(name, SqlDbType.NVarChar, size) { Value = (object?)value ?? DBNull.Value };
    }
}
