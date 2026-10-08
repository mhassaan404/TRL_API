using System.Data;
using Microsoft.Data.SqlClient;
using TRL_API.Data;
using TRL_API.Models;

namespace TRL_API.DAL
{
    // Maintenance jobs. Every change runs in one transaction with the job row locked and writes a MaintenanceLog row.
    public class MaintenanceRepository : IMaintenanceRepository
    {
        private readonly DbHelper _dbHelper;
        public MaintenanceRepository(DbHelper dbHelper) => _dbHelper = dbHelper;

        public async Task<DataTable> GetAllAsync() =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT j.Id, j.BuildingId, b.BuildingName, j.FloorId, f.FloorNumber, j.UnitId, u.UnitNumber,
                       j.TenantId, t.Name AS TenantName, j.Title, j.Description, j.Category, j.Priority, j.Status,
                       j.AssignedTo, j.ReportedDate, j.CompletedDate, j.Cost, j.MarkedUnit,
                       j.ChargeInvoiceId, ri.TotalRent AS ChargeAmount, sl.StatusName AS ChargeStatus,
                       -- Billable again only when there is no invoice or it was cancelled
                       CAST(CASE WHEN j.ChargeInvoiceId IS NULL OR ri.StatusId = 6 THEN 0 ELSE 1 END AS BIT) AS IsBilled,
                       j.CreatedAt, j.UpdatedAt
                FROM MaintenanceJobs j
                JOIN Buildings b ON b.BuildingId = j.BuildingId
                LEFT JOIN Floors f ON f.FloorId = j.FloorId
                LEFT JOIN Units u ON u.UnitId = j.UnitId
                LEFT JOIN Tenants t ON t.TenantId = j.TenantId
                LEFT JOIN RentInvoices ri ON ri.Id = j.ChargeInvoiceId
                LEFT JOIN StatusList sl ON sl.StatusId = ri.StatusId
                ORDER BY j.Id DESC;");

        public async Task<DataTable> GetLogAsync(int jobId) =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT l.Id, l.Action, l.Note, l.CreatedAt, us.Username AS UserName
                FROM MaintenanceLog l
                LEFT JOIN Users us ON us.UserId = l.UserId
                WHERE l.JobId = @JobId
                ORDER BY l.CreatedAt, l.Id;", new[] { new SqlParameter("@JobId", jobId) });

        // Active buildings (a job can be for a building's common area, without a unit)
        public async Task<DataTable> GetBuildingsAsync() =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT BuildingId, BuildingName FROM Buildings WHERE IsActive = 1 ORDER BY BuildingName;");

        // Active units with their floor and the tenant of the unit's current lease (the earliest active one), if any
        public async Task<DataTable> GetUnitsAsync() =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT b.BuildingId, b.BuildingName, f.FloorId, f.FloorNumber, u.UnitId, u.UnitNumber, u.StatusId AS UnitStatusId,
                       lt.TenantId, lt.TenantName
                FROM Units u
                JOIN Floors f ON f.FloorId = u.FloorId
                JOIN Buildings b ON b.BuildingId = f.BuildingId
                OUTER APPLY (SELECT TOP 1 tl.TenantId, t.Name AS TenantName
                             FROM TenantLeases tl JOIN Tenants t ON t.TenantId = tl.TenantId
                             WHERE tl.UnitId = u.UnitId AND tl.IsActive = 1
                             ORDER BY tl.StartDate) lt
                WHERE u.IsActive = 1 AND f.IsActive = 1 AND b.IsActive = 1
                ORDER BY b.BuildingName, f.FloorNumber, u.UnitNumber;");

        // Location checks shared by Create and Update: the floor is in the building, the unit is on the floor,
        // all active, and the tenant exists. Sets @Bad to a result code.
        private const string CheckLocationSql = @"
                IF NOT EXISTS (SELECT 1 FROM Buildings WHERE BuildingId = @BuildingId AND IsActive = 1)
                    SET @Bad = 'BAD_BUILDING';
                ELSE IF @FloorId IS NOT NULL AND NOT EXISTS (
                        SELECT 1 FROM Floors WHERE FloorId = @FloorId AND BuildingId = @BuildingId AND IsActive = 1)
                    SET @Bad = 'BAD_FLOOR';
                ELSE IF @UnitId IS NOT NULL AND NOT EXISTS (
                        SELECT 1 FROM Units WHERE UnitId = @UnitId AND FloorId = @FloorId AND IsActive = 1)
                    SET @Bad = 'BAD_UNIT';
                ELSE IF @TenantId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM Tenants WHERE TenantId = @TenantId AND IsDeleted = 0)
                    SET @Bad = 'BAD_TENANT';";

        private static SqlParameter[] JobParameters(MaintenanceJobRequest req, DateTime reportedDate, int userId) => new[]
        {
            new SqlParameter("@Id", req.Id),
            new SqlParameter("@BuildingId", req.BuildingId),
            new SqlParameter("@FloorId", SqlDbType.Int) { Value = (object?)req.FloorId ?? DBNull.Value },
            new SqlParameter("@UnitId", SqlDbType.Int) { Value = (object?)req.UnitId ?? DBNull.Value },
            new SqlParameter("@TenantId", SqlDbType.Int) { Value = (object?)req.TenantId ?? DBNull.Value },
            new SqlParameter("@Title", SqlDbType.NVarChar, 150) { Value = req.Title },
            new SqlParameter("@Description", SqlDbType.NVarChar, 1000) { Value = (object?)req.Description ?? DBNull.Value },
            new SqlParameter("@Category", SqlDbType.NVarChar, 30) { Value = req.Category },
            new SqlParameter("@Priority", SqlDbType.NVarChar, 10) { Value = req.Priority },
            new SqlParameter("@AssignedTo", SqlDbType.NVarChar, 100) { Value = (object?)req.AssignedTo ?? DBNull.Value },
            new SqlParameter("@ReportedDate", SqlDbType.Date) { Value = reportedDate.Date },
            new SqlParameter("@Cost", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = (object?)req.Cost ?? DBNull.Value },
            new SqlParameter("@Mark", SqlDbType.Bit) { Value = req.MarkUnitUnderMaintenance },
            new SqlParameter("@UserId", userId),
        };

        public async Task<(string Result, int? Id)> CreateAsync(MaintenanceJobRequest req, DateTime reportedDate, int userId)
        {
            string query = @"
                SET XACT_ABORT ON;
                DECLARE @Bad VARCHAR(20), @NewId INT, @UnitStatus INT;
                BEGIN TRAN;
                " + CheckLocationSql + @"
                IF @Bad IS NOT NULL BEGIN ROLLBACK; SELECT @Bad AS Result, CAST(NULL AS INT) AS Id; RETURN; END

                INSERT INTO MaintenanceJobs (BuildingId, FloorId, UnitId, TenantId, Title, Description, Category, Priority,
                                             Status, AssignedTo, ReportedDate, Cost, CreatedBy)
                VALUES (@BuildingId, @FloorId, @UnitId, @TenantId, @Title, @Description, @Category, @Priority,
                        'Open', @AssignedTo, @ReportedDate, @Cost, @UserId);
                SET @NewId = SCOPE_IDENTITY();
                INSERT INTO MaintenanceLog (JobId, Action, UserId) VALUES (@NewId, 'Created', @UserId);

                -- Optionally mark the unit Under Maintenance (status 4), remembering its status to restore on close.
                -- A unit that is already Under Maintenance is left as it is (this job doesn't take over the mark).
                IF @Mark = 1 AND @UnitId IS NOT NULL
                BEGIN
                    SELECT @UnitStatus = StatusId FROM Units WITH (UPDLOCK) WHERE UnitId = @UnitId;
                    IF ISNULL(@UnitStatus, 0) <> 4
                    BEGIN
                        UPDATE Units SET StatusId = 4 WHERE UnitId = @UnitId;
                        UPDATE MaintenanceJobs SET MarkedUnit = 1, UnitStatusBefore = @UnitStatus WHERE Id = @NewId;
                        INSERT INTO MaintenanceLog (JobId, Action, Note, UserId)
                        VALUES (@NewId, 'Unit marked', N'Unit set to Under Maintenance', @UserId);
                    END
                END
                COMMIT;
                SELECT 'OK' AS Result, @NewId AS Id;";

            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(query, JobParameters(req, reportedDate, userId));
            var row = dt.Rows[0];
            return (row["Result"].ToString()!, row["Id"] == DBNull.Value ? null : Convert.ToInt32(row["Id"]));
        }

        // Edits the details. A cancelled job can't be edited. The location and tenant can't change once the job was
        // billed (the invoice belongs to them) or while it holds the unit's Under Maintenance mark.
        public async Task<string> UpdateAsync(MaintenanceJobRequest req, DateTime reportedDate, int userId)
        {
            string query = @"
                SET XACT_ABORT ON;
                DECLARE @Bad VARCHAR(20), @Status NVARCHAR(20), @OldB INT, @OldF INT, @OldU INT, @OldT INT, @Marked BIT, @Billed BIT;
                BEGIN TRAN;
                SELECT @Status = j.Status, @OldB = j.BuildingId, @OldF = j.FloorId, @OldU = j.UnitId, @OldT = j.TenantId,
                       @Marked = j.MarkedUnit,
                       @Billed = CASE WHEN j.ChargeInvoiceId IS NOT NULL AND ISNULL(ri.StatusId, 0) <> 6 THEN 1 ELSE 0 END
                FROM MaintenanceJobs j WITH (UPDLOCK, HOLDLOCK)
                LEFT JOIN RentInvoices ri ON ri.Id = j.ChargeInvoiceId
                WHERE j.Id = @Id;
                IF @Status IS NULL BEGIN ROLLBACK; SELECT 'NOT_FOUND' AS Result; RETURN; END
                IF @Status = 'Cancelled' BEGIN ROLLBACK; SELECT 'CANCELLED' AS Result; RETURN; END

                IF (@Billed = 1 OR @Marked = 1)
                   AND (@OldB <> @BuildingId OR ISNULL(@OldF, 0) <> ISNULL(@FloorId, 0)
                        OR ISNULL(@OldU, 0) <> ISNULL(@UnitId, 0) OR ISNULL(@OldT, 0) <> ISNULL(@TenantId, 0))
                BEGIN ROLLBACK; SELECT CASE WHEN @Billed = 1 THEN 'LOCKED_BILLED' ELSE 'LOCKED_MARKED' END AS Result; RETURN; END

                " + CheckLocationSql + @"
                IF @Bad IS NOT NULL BEGIN ROLLBACK; SELECT @Bad AS Result; RETURN; END

                UPDATE MaintenanceJobs
                SET BuildingId = @BuildingId, FloorId = @FloorId, UnitId = @UnitId, TenantId = @TenantId, Title = @Title,
                    Description = @Description, Category = @Category, Priority = @Priority, AssignedTo = @AssignedTo,
                    ReportedDate = @ReportedDate, Cost = @Cost, UpdatedAt = GETDATE()
                WHERE Id = @Id;
                INSERT INTO MaintenanceLog (JobId, Action, UserId) VALUES (@Id, 'Updated', @UserId);
                COMMIT;
                SELECT 'OK' AS Result;";

            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(query, JobParameters(req, reportedDate, userId));
            return dt.Rows[0]["Result"].ToString()!;
        }

        // Open <-> In Progress, and either to Completed or Cancelled (final). Closing a job that marked its unit
        // Under Maintenance puts the unit back to its earlier status, if it is still Under Maintenance.
        public async Task<string> ChangeStatusAsync(int id, string status, string? note, DateTime? completedDate, int userId)
        {
            string query = @"
                SET XACT_ABORT ON;
                DECLARE @Old NVARCHAR(20), @Reported DATE, @UnitId INT, @Marked BIT, @Before INT;
                BEGIN TRAN;
                SELECT @Old = Status, @Reported = ReportedDate, @UnitId = UnitId, @Marked = MarkedUnit, @Before = UnitStatusBefore
                FROM MaintenanceJobs WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id;
                IF @Old IS NULL BEGIN ROLLBACK; SELECT 'NOT_FOUND' AS Result; RETURN; END
                IF @Old IN ('Completed', 'Cancelled') BEGIN ROLLBACK; SELECT 'FINAL' AS Result; RETURN; END
                IF @Old = @Status BEGIN ROLLBACK; SELECT 'NO_CHANGE' AS Result; RETURN; END
                IF @Status = 'Completed' AND @Completed < @Reported BEGIN ROLLBACK; SELECT 'BEFORE_REPORTED' AS Result; RETURN; END

                UPDATE MaintenanceJobs
                SET Status = @Status, CompletedDate = CASE WHEN @Status = 'Completed' THEN @Completed END, UpdatedAt = GETDATE()
                WHERE Id = @Id;
                INSERT INTO MaintenanceLog (JobId, Action, Note, UserId) VALUES (@Id, CONCAT(N'Status: ', @Status), @Note, @UserId);

                IF @Status IN ('Completed', 'Cancelled') AND @Marked = 1
                BEGIN
                    UPDATE Units SET StatusId = ISNULL(@Before, 1) WHERE UnitId = @UnitId AND StatusId = 4;
                    IF @@ROWCOUNT > 0
                        INSERT INTO MaintenanceLog (JobId, Action, Note, UserId)
                        VALUES (@Id, 'Unit restored', N'Unit no longer Under Maintenance', @UserId);
                    UPDATE MaintenanceJobs SET MarkedUnit = 0 WHERE Id = @Id;
                END
                COMMIT;
                SELECT 'OK' AS Result;";

            var parameters = new[]
            {
                new SqlParameter("@Id", id),
                new SqlParameter("@Status", SqlDbType.NVarChar, 20) { Value = status },
                new SqlParameter("@Note", SqlDbType.NVarChar, 500) { Value = (object?)note ?? DBNull.Value },
                new SqlParameter("@Completed", SqlDbType.Date) { Value = (object?)completedDate?.Date ?? DBNull.Value },
                new SqlParameter("@UserId", userId),
            };
            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
            return dt.Rows[0]["Result"].ToString()!;
        }

        // Creates a Maintenance extra-charge invoice for the job's tenant and unit (same columns as Extra Charge:
        // unpaid, not linked to a lease, late fee rate snapshot) and links it to the job. A job is billed once; it
        // can be billed again only after that invoice was cancelled.
        public async Task<(string Result, int? InvoiceId)> BillAsync(int id, decimal amount, string description, DateTime dueDate,
            decimal lateFeePerDay, decimal lateFeeMaxMultiplier, int userId)
        {
            string query = @"
                SET XACT_ABORT ON;
                DECLARE @Status NVARCHAR(20), @TenantId INT, @UnitId INT, @ChargeId INT, @ChargeStatus INT, @InvoiceId INT;
                DECLARE @New TABLE (Id INT);
                BEGIN TRAN;
                SELECT @Status = j.Status, @TenantId = j.TenantId, @UnitId = j.UnitId, @ChargeId = j.ChargeInvoiceId,
                       @ChargeStatus = ri.StatusId
                FROM MaintenanceJobs j WITH (UPDLOCK, HOLDLOCK)
                LEFT JOIN RentInvoices ri ON ri.Id = j.ChargeInvoiceId
                WHERE j.Id = @Id;
                IF @Status IS NULL BEGIN ROLLBACK; SELECT 'NOT_FOUND' AS Result, CAST(NULL AS INT) AS InvoiceId; RETURN; END
                IF @Status = 'Cancelled' BEGIN ROLLBACK; SELECT 'CANCELLED' AS Result, CAST(NULL AS INT) AS InvoiceId; RETURN; END
                IF @TenantId IS NULL BEGIN ROLLBACK; SELECT 'NO_TENANT' AS Result, CAST(NULL AS INT) AS InvoiceId; RETURN; END
                IF @ChargeId IS NOT NULL AND ISNULL(@ChargeStatus, 0) <> 6
                BEGIN ROLLBACK; SELECT 'ALREADY_BILLED' AS Result, @ChargeId AS InvoiceId; RETURN; END
                IF NOT EXISTS (SELECT 1 FROM Tenants WHERE TenantId = @TenantId AND IsDeleted = 0)
                BEGIN ROLLBACK; SELECT 'BAD_TENANT' AS Result, CAST(NULL AS INT) AS InvoiceId; RETURN; END

                INSERT INTO RentInvoices
                    (TenantId, LeaseId, UnitId, TotalRent, PendingAmount, OverPaidAmount, InvoiceDate, DueDate, StatusId,
                     Description, ChargeType, CreatedAt, LateFeePerDay, LateFeeMaxMultiplier)
                OUTPUT inserted.Id INTO @New
                SELECT @TenantId, NULL, ISNULL(@UnitId, t.UnitId), @Amount, @Amount, 0, CAST(GETDATE() AS DATE), @DueDate, 2,
                       @Description, 'Maintenance', GETDATE(), @LateFeePerDay, @LateFeeMaxMultiplier
                FROM Tenants t WHERE t.TenantId = @TenantId;
                SELECT @InvoiceId = Id FROM @New;

                UPDATE MaintenanceJobs SET ChargeInvoiceId = @InvoiceId, UpdatedAt = GETDATE() WHERE Id = @Id;
                INSERT INTO MaintenanceLog (JobId, Action, Note, UserId)
                VALUES (@Id, 'Billed', CONCAT(N'Invoice #', @InvoiceId, N', PKR ', FORMAT(@Amount, 'N0')), @UserId);
                COMMIT;
                SELECT 'OK' AS Result, @InvoiceId AS InvoiceId;";

            var parameters = new[]
            {
                new SqlParameter("@Id", id),
                new SqlParameter("@Amount", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = amount },
                new SqlParameter("@Description", SqlDbType.NVarChar, 255) { Value = description },
                new SqlParameter("@DueDate", SqlDbType.Date) { Value = dueDate.Date },
                new SqlParameter("@LateFeePerDay", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = lateFeePerDay },
                new SqlParameter("@LateFeeMaxMultiplier", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = lateFeeMaxMultiplier },
                new SqlParameter("@UserId", userId),
            };
            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
            var row = dt.Rows[0];
            return (row["Result"].ToString()!, row["InvoiceId"] == DBNull.Value ? null : Convert.ToInt32(row["InvoiceId"]));
        }
    }
}
