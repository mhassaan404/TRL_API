using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.Data;
using TRL_API.Models;

namespace TRL_API.DAL
{
    public class LeaseRepository : ILeaseRepository
    {
        private readonly DbHelper _dbHelper;
        public LeaseRepository(DbHelper dbHelper) => _dbHelper = dbHelper;

        public async Task<DataTable> GetAllAsync()
        {
            string query = @"
                SELECT tl.LeaseId, tl.TenantId, t.Name AS TenantName, tl.UnitId, u.UnitNumber,
                       b.BuildingName, f.FloorNumber, tl.RentAmount, tl.StartDate, tl.EndDate,
                       tl.TenureMonths, tl.IsActive,
                       CASE WHEN tl.IsActive = 1 AND tl.EndDate < CAST(GETDATE() AS DATE) THEN 1 ELSE 0 END AS IsExpired,
                       -- For the list's status only (display): how far the lease is billed, why it stopped, and
                       -- where a renewed lease's next term starts
                       tl.BilledThrough, tl.TerminationReason,
                       (SELECT MIN(n.StartDate) FROM TenantLeases n
                        WHERE n.TenantId = tl.TenantId AND n.UnitId = tl.UnitId AND n.StartDate > tl.StartDate) AS NextStartDate,
                       -- A renewed lease is still the current term while its hand-over is intact: the next term
                       -- starts the day after it is billed through and that term has not been ended (it is
                       -- active, or itself renewed). Ending the tenancy breaks the hand-over.
                       CAST(CASE WHEN tl.IsActive = 0 AND tl.TerminationReason = 'Renewed' AND EXISTS (
                                SELECT 1 FROM TenantLeases n
                                WHERE n.TenantId = tl.TenantId AND n.UnitId = tl.UnitId
                                  AND n.StartDate = DATEADD(DAY, 1, tl.BilledThrough)
                                  AND (n.IsActive = 1 OR n.TerminationReason = 'Renewed'))
                            THEN 1 ELSE 0 END AS BIT) AS RenewedIntoNext,
                       -- An upcoming renewal that a current lease hands over to: it can be cancelled (CancelRenewalAsync)
                       CAST(CASE WHEN tl.IsActive = 1 AND tl.StartDate > CAST(GETDATE() AS DATE) AND EXISTS (
                                SELECT 1 FROM TenantLeases p
                                WHERE p.TenantId = tl.TenantId AND p.UnitId = tl.UnitId AND p.IsActive = 0
                                  AND p.TerminationReason = 'Renewed' AND p.BilledThrough = DATEADD(DAY, -1, tl.StartDate))
                            THEN 1 ELSE 0 END AS BIT) AS IsPendingRenewal
                FROM TenantLeases tl
                JOIN Tenants t ON t.TenantId = tl.TenantId
                JOIN Units u ON u.UnitId = tl.UnitId
                JOIN Floors f ON f.FloorId = u.FloorId
                JOIN Buildings b ON b.BuildingId = f.BuildingId
                ORDER BY tl.IsActive DESC, tl.StartDate DESC;";
            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query);
        }

        public async Task<DataTable> GetByTenantAsync(int tenantId)
        {
            string query = @"
                SELECT tl.LeaseId, tl.UnitId, u.UnitNumber, b.BuildingName, f.FloorNumber,
                       tl.RentAmount, tl.StartDate, tl.EndDate, tl.TenureMonths, tl.IsActive
                FROM TenantLeases tl
                JOIN Units u ON u.UnitId = tl.UnitId
                JOIN Floors f ON f.FloorId = u.FloorId
                JOIN Buildings b ON b.BuildingId = f.BuildingId
                WHERE tl.TenantId = @TenantId
                ORDER BY tl.IsActive DESC, tl.StartDate DESC;";
            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, new[] { new SqlParameter("@TenantId", tenantId) });
        }

        // Last day an ended lease on this unit is still billed, if that reaches the proposed start date
        // (a new lease starting then would bill the same unit twice). Active leases are covered by UX_TenantLeases_ActiveUnit.
        public async Task<DateTime?> GetOverlappingBilledThroughAsync(int unitId, DateTime startDate)
        {
            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT MAX(BilledThrough) AS BilledThrough FROM TenantLeases
                WHERE UnitId = @UnitId AND IsActive = 0 AND BilledThrough >= @StartDate AND BilledThrough >= StartDate;",
                new[] { new SqlParameter("@UnitId", unitId), new SqlParameter("@StartDate", SqlDbType.Date) { Value = startDate.Date } });
            return dt.Rows.Count > 0 && dt.Rows[0]["BilledThrough"] != DBNull.Value ? Convert.ToDateTime(dt.Rows[0]["BilledThrough"]) : null;
        }

        public async Task<ApiResponse> CreateAsync(Lease lease, int userId)
        {
            // Tenants holds the tenant's current unit and rent, so the lease and the tenant row change together.
            string query = @"
                SET XACT_ABORT ON;
                BEGIN TRAN;
                INSERT INTO TenantLeases (TenantId, UnitId, RentAmount, StartDate, TenureMonths, IsActive, CreatedBy)
                VALUES (@TenantId, @UnitId, @RentAmount, @StartDate, @TenureMonths, 1, @CreatedBy);

                UPDATE t SET UnitId = u.UnitId, FloorId = u.FloorId, BuildingId = u.BuildingId, MonthlyRent = @RentAmount,
                             IsActive = 1, MoveOutDate = NULL
                FROM Tenants t JOIN Units u ON u.UnitId = @UnitId
                WHERE t.TenantId = @TenantId;
                COMMIT;";

            var parameters = new[]
            {
                new SqlParameter("@TenantId", lease.TenantId),
                new SqlParameter("@UnitId", lease.UnitId),
                new SqlParameter("@RentAmount", lease.RentAmount),
                new SqlParameter("@StartDate", lease.StartDate),
                new SqlParameter("@TenureMonths", lease.TenureMonths),
                new SqlParameter("@CreatedBy", userId),
            };
            return await _dbHelper.ExecuteQueryAsync(query, parameters);
        }

        // Runs after a lease's BilledThrough moved earlier (terminate/renew), for the leases in @Affected.
        // Only invoices from the lease's last billed month onwards are touched (earlier months are unaffected).
        // Each is re-priced to what the lease actually covered. Decisions use cash actually paid (SUM(PaymentAmount)),
        // never discounts or waivers:
        //   - no longer covered and no cash paid  -> cancelled; its discounts are voided (a discount on a month that is
        //     not billed is meaningless)
        //   - otherwise re-priced; discounts are capped to what is still owed after the cash, so only real cash can
        //     ever become a credit (overpaid). Payment rows are never deleted.
        private const string AdjustInvoicesAfterEndSql = @"
                DECLARE @Adj TABLE (InvoiceId INT PRIMARY KEY, NewAmount DECIMAL(18,2), Descr NVARCHAR(200), Cash DECIMAL(18,2), Fee DECIMAL(18,2));
                INSERT INTO @Adj
                SELECT ri.Id, c.Amount, c.Descr,
                       (SELECT ISNULL(SUM(p.PaymentAmount), 0) FROM Payments p WHERE p.RentInvoiceId = ri.Id),
                       ri.LateFeeCharged
                FROM RentInvoices ri
                JOIN @Affected a ON a.LeaseId = ri.LeaseId
                JOIN TenantLeases l ON l.LeaseId = ri.LeaseId
                CROSS APPLY dbo.LeaseMonthCharge(ri.LeaseId, ri.InvoiceMonth) c
                WHERE ri.ChargeType IS NULL AND ri.StatusId <> 6
                  AND l.BilledThrough IS NOT NULL
                  AND ri.InvoiceMonth >= DATEFROMPARTS(YEAR(l.BilledThrough), MONTH(l.BilledThrough), 1)
                  AND c.Amount <> ri.TotalRent;

                -- Discounts allowed per invoice: nothing on a cancelled month, otherwise at most what the cash leaves owed.
                -- Oldest discounts are kept first.
                DECLARE @Disc TABLE (PaymentId INT PRIMARY KEY, InvoiceId INT, OldDisc DECIMAL(18,2), NewDisc DECIMAL(18,2));
                INSERT INTO @Disc
                SELECT d.Id, d.RentInvoiceId, d.DiscountAmount,
                       CASE WHEN d.Cum <= d.Allowed THEN d.DiscountAmount
                            WHEN d.Cum - d.DiscountAmount >= d.Allowed THEN 0
                            ELSE d.Allowed - (d.Cum - d.DiscountAmount) END
                FROM (SELECT p.Id, p.RentInvoiceId, p.DiscountAmount,
                             SUM(p.DiscountAmount) OVER (PARTITION BY p.RentInvoiceId ORDER BY p.Id) AS Cum,
                             CASE WHEN x.NewAmount = 0 AND x.Cash <= 0 THEN 0
                                  WHEN x.NewAmount + x.Fee - x.Cash > 0 THEN x.NewAmount + x.Fee - x.Cash
                                  ELSE 0 END AS Allowed
                      FROM Payments p JOIN @Adj x ON x.InvoiceId = p.RentInvoiceId
                      WHERE p.DiscountAmount > 0) d;
                DELETE FROM @Disc WHERE NewDisc = OldDisc;

                UPDATE p SET DiscountAmount = d.NewDisc, UpdatedBy = @UserId, UpdatedAt = GETDATE()
                FROM Payments p JOIN @Disc d ON d.PaymentId = p.Id;

                INSERT INTO InvoiceAudit (InvoiceId, Action, Amount, Reason, CreatedBy)
                SELECT InvoiceId, 'DISCOUNT_REDUCED', SUM(OldDisc - NewDisc), @AdjReason, @UserId FROM @Disc GROUP BY InvoiceId;

                UPDATE ri SET StatusId = 6
                FROM RentInvoices ri JOIN @Adj x ON x.InvoiceId = ri.Id
                WHERE x.NewAmount = 0 AND x.Cash <= 0;

                INSERT INTO InvoiceAudit (InvoiceId, Action, Amount, Reason, CreatedBy)
                SELECT InvoiceId, 'CANCELLED', 0, @AdjReason, @UserId FROM @Adj WHERE NewAmount = 0 AND Cash <= 0;

                DELETE FROM @Adj WHERE NewAmount = 0 AND Cash <= 0;

                UPDATE ri SET TotalRent = x.NewAmount, Description = x.Descr
                FROM RentInvoices ri JOIN @Adj x ON x.InvoiceId = ri.Id;

                INSERT INTO InvoiceAudit (InvoiceId, Action, Amount, Reason, CreatedBy)
                SELECT InvoiceId, 'RENT_ADJUSTED', NewAmount, @AdjReason, @UserId FROM @Adj;

                -- Recalculate pending/overpaid/status (InvoiceSql.Recalc) for the corrected invoices
                " + InvoiceSql.Recalc + @"
                JOIN @Adj x ON x.InvoiceId = ri.Id;";

        // Ends the current lease and starts a fresh one on the same unit (keeps full history).
        // The new lease starts when the old term ends (or today if it has already expired, i.e. after the
        // month-to-month period), and the old lease is billed through the day before, so rents never overlap.
        public async Task<ApiResponse> RenewAsync(RenewLeaseRequest req, int userId)
        {
            string query = @"
                SET XACT_ABORT ON;
                DECLARE @TenantId INT, @UnitId INT, @OldRent DECIMAL(18,2), @OldStart DATE, @OldEnd DATE, @NewStart DATE,
                        @Today DATE = CAST(GETDATE() AS DATE), @AdjReason NVARCHAR(300) = N'Lease renewed';

                -- Lock the lease row: a second simultaneous renew waits here, then finds the lease no longer active
                BEGIN TRAN;
                SELECT @TenantId = TenantId, @UnitId = UnitId, @OldRent = RentAmount, @OldStart = StartDate, @OldEnd = EndDate
                FROM TenantLeases WITH (UPDLOCK, HOLDLOCK) WHERE LeaseId = @LeaseId AND IsActive = 1;

                IF @TenantId IS NULL
                BEGIN
                    ROLLBACK;
                    SELECT 'NOT_FOUND' AS Result, CAST(NULL AS DATE) AS NewStart; RETURN;
                END

                -- Only a term that has started can be renewed. A renewal creates the next term, which starts in the
                -- future when renewed early; renewing that unstarted term again would stack terms years ahead.
                -- So there is at most one upcoming term, and it can be renewed once it begins.
                IF @OldStart > @Today
                BEGIN
                    ROLLBACK;
                    SELECT 'NOT_STARTED' AS Result, @OldStart AS NewStart; RETURN;
                END

                SET @NewStart = CASE WHEN @OldEnd > @Today THEN @OldEnd ELSE @Today END;

                UPDATE TenantLeases SET IsActive = 0, TerminatedAt = GETDATE(), TerminationReason = 'Renewed',
                       BilledThrough = DATEADD(DAY, -1, @NewStart)
                WHERE LeaseId = @LeaseId;

                INSERT INTO TenantLeases (TenantId, UnitId, RentAmount, StartDate, TenureMonths, IsActive, CreatedBy)
                VALUES (@TenantId, @UnitId, ISNULL(@NewRent, @OldRent), @NewStart, @TenureMonths, 1, @CreatedBy);

                UPDATE Tenants SET MonthlyRent = ISNULL(@NewRent, @OldRent) WHERE TenantId = @TenantId AND UnitId = @UnitId;

                DECLARE @Affected TABLE (LeaseId INT);
                INSERT INTO @Affected VALUES (@LeaseId);
                " + AdjustInvoicesAfterEndSql + @"
                COMMIT;

                SELECT 'OK' AS Result, @NewStart AS NewStart;";

            var parameters = new[]
            {
                new SqlParameter("@LeaseId", req.LeaseId),
                new SqlParameter("@NewRent", (object?)req.NewRentAmount ?? DBNull.Value),
                new SqlParameter("@TenureMonths", req.TenureMonths),
                new SqlParameter("@CreatedBy", userId),
                new SqlParameter("@UserId", userId),
            };
            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
            var result = dt.Rows.Count > 0 ? dt.Rows[0]["Result"].ToString() : null;
            if (result == "OK")
                return new ApiResponse { IsSuccess = true, Message = $"Lease renewed. The new term starts {Convert.ToDateTime(dt.Rows[0]["NewStart"]):dd MMM yyyy}." };
            if (result == "NOT_STARTED")
                return new ApiResponse
                {
                    IsSuccess = false,
                    ErrorMessage = $"This lease term hasn't started yet (it starts {Convert.ToDateTime(dt.Rows[0]["NewStart"]):dd MMM yyyy}), so it can't be renewed. A lease can be renewed once its term has started.",
                };
            return new ApiResponse { IsSuccess = false, ErrorMessage = "Active lease not found." };
        }

        // Cancels an early renewal that hasn't started: the renewal is marked cancelled (never billed) and the lease it
        // replaced becomes the open lease again, running to its end date and month to month after that, exactly as if
        // it had not been renewed. Ending the tenancy is a different action (TerminateAsync).
        // Refused when the renewal has started, when it isn't a renewal (no lease handing over to it), or when
        // payments, discounts or waivers are recorded on its invoices (reverse those first).
        public async Task<ApiResponse> CancelRenewalAsync(int leaseId, int userId)
        {
            string query = @"
                SET XACT_ABORT ON;
                DECLARE @TenantId INT, @UnitId INT, @Start DATE, @PrevId INT, @PrevRent DECIMAL(18,2), @PrevEnd DATE,
                        @Today DATE = CAST(GETDATE() AS DATE), @AdjReason NVARCHAR(300) = N'Renewal cancelled';

                -- Lock the renewal and the lease it replaced, like Renew/Terminate do
                BEGIN TRAN;
                SELECT @TenantId = TenantId, @UnitId = UnitId, @Start = StartDate
                FROM TenantLeases WITH (UPDLOCK, HOLDLOCK) WHERE LeaseId = @LeaseId AND IsActive = 1;
                IF @TenantId IS NULL BEGIN ROLLBACK; SELECT 'NOT_FOUND' AS Result, CAST(NULL AS DATE) AS PrevEnd; RETURN; END
                IF @Start <= @Today BEGIN ROLLBACK; SELECT 'STARTED' AS Result, CAST(NULL AS DATE) AS PrevEnd; RETURN; END

                SELECT @PrevId = LeaseId, @PrevRent = RentAmount, @PrevEnd = EndDate
                FROM TenantLeases WITH (UPDLOCK, HOLDLOCK)
                WHERE TenantId = @TenantId AND UnitId = @UnitId AND IsActive = 0
                  AND TerminationReason = 'Renewed' AND BilledThrough = DATEADD(DAY, -1, @Start);
                IF @PrevId IS NULL BEGIN ROLLBACK; SELECT 'NOT_RENEWAL' AS Result, CAST(NULL AS DATE) AS PrevEnd; RETURN; END

                IF EXISTS (SELECT 1 FROM RentInvoices ri JOIN Payments p ON p.RentInvoiceId = ri.Id
                           WHERE ri.LeaseId = @LeaseId AND ri.ChargeType IS NULL)
                BEGIN ROLLBACK; SELECT 'HAS_PAYMENTS' AS Result, CAST(NULL AS DATE) AS PrevEnd; RETURN; END

                -- Renewal first (one active lease per unit), billed through the day before it starts = never billed
                UPDATE TenantLeases
                SET IsActive = 0, TerminatedAt = GETDATE(), TerminationReason = 'Renewal cancelled',
                    BilledThrough = DATEADD(DAY, -1, StartDate)
                WHERE LeaseId = @LeaseId;

                UPDATE TenantLeases
                SET IsActive = 1, TerminatedAt = NULL, TerminationReason = NULL, BilledThrough = NULL
                WHERE LeaseId = @PrevId;

                UPDATE Tenants SET MonthlyRent = @PrevRent WHERE TenantId = @TenantId AND UnitId = @UnitId;

                -- The renewal's invoices (none can have payments) are cancelled by the usual correction
                DECLARE @Affected TABLE (LeaseId INT);
                INSERT INTO @Affected VALUES (@LeaseId);
                " + AdjustInvoicesAfterEndSql + @"

                -- The restored lease covers its months in full again: re-price invoices the renewal had shortened
                -- (the change-over month). The amounts only go up, so no discount needs reducing.
                DECLARE @Up TABLE (InvoiceId INT PRIMARY KEY, NewAmount DECIMAL(18,2), Descr NVARCHAR(200));
                INSERT INTO @Up
                SELECT ri.Id, c.Amount, c.Descr
                FROM RentInvoices ri
                CROSS APPLY dbo.LeaseMonthCharge(ri.LeaseId, ri.InvoiceMonth) c
                WHERE ri.LeaseId = @PrevId AND ri.ChargeType IS NULL AND ri.StatusId <> 6 AND c.Amount <> ri.TotalRent;

                UPDATE ri SET TotalRent = u.NewAmount, Description = u.Descr
                FROM RentInvoices ri JOIN @Up u ON u.InvoiceId = ri.Id;

                INSERT INTO InvoiceAudit (InvoiceId, Action, Amount, Reason, CreatedBy)
                SELECT InvoiceId, 'RENT_ADJUSTED', NewAmount, @AdjReason, @UserId FROM @Up;

                " + InvoiceSql.Recalc + @"
                JOIN @Up u ON u.InvoiceId = ri.Id;

                COMMIT;
                SELECT 'OK' AS Result, @PrevEnd AS PrevEnd;";

            var parameters = new[]
            {
                new SqlParameter("@LeaseId", leaseId),
                new SqlParameter("@UserId", userId),
            };
            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
            var result = dt.Rows.Count > 0 ? dt.Rows[0]["Result"].ToString() : null;
            return result switch
            {
                "OK" => new ApiResponse
                {
                    IsSuccess = true,
                    Message = $"Renewal cancelled. The current lease continues to {Convert.ToDateTime(dt.Rows[0]["PrevEnd"]):dd MMM yyyy} (month to month after that until renewed or ended).",
                },
                "STARTED" => new ApiResponse { IsSuccess = false, ErrorMessage = "This term has already started, so it can't be cancelled as a renewal. Use End to end the lease." },
                "NOT_RENEWAL" => new ApiResponse { IsSuccess = false, ErrorMessage = "This lease isn't a renewal of a current lease, so there's nothing to restore. Use End to cancel it." },
                "HAS_PAYMENTS" => new ApiResponse { IsSuccess = false, ErrorMessage = "Payments, discounts or waivers are recorded on this renewal's invoices. Reverse them first." },
                _ => new ApiResponse { IsSuccess = false, ErrorMessage = "Active lease not found." },
            };
        }

        // Ends the lease on the move-out date: rent is billed through that day and stops after it.
        // Moving out on/before the start date means the lease never started, so nothing is billed.
        public async Task<ApiResponse> TerminateAsync(TerminateLeaseRequest req, DateTime moveOutDate, int userId)
        {
            string query = @"
                SET XACT_ABORT ON;
                DECLARE @TenantId INT, @UnitId INT, @Start DATE, @Today DATE = CAST(GETDATE() AS DATE),
                        @AdjReason NVARCHAR(300) = CONCAT(N'Lease ended: ', @Reason);

                -- Lock the lease row: a second simultaneous terminate waits here, then finds the lease already ended
                -- and changes nothing, so leases can't end up with different end dates.
                BEGIN TRAN;
                SELECT @TenantId = TenantId, @UnitId = UnitId, @Start = StartDate
                FROM TenantLeases WITH (UPDLOCK, HOLDLOCK) WHERE LeaseId = @LeaseId AND IsActive = 1;
                IF @TenantId IS NULL BEGIN ROLLBACK; SELECT 'NOT_FOUND' AS Result, @Start AS StartDate; RETURN; END

                -- A lease that has started can't be ended before its start date (to undo a lease made by mistake,
                -- use Cancel Lease). A lease that hasn't started yet is ended before its start, so nothing is billed.
                IF @Start <= @Today AND @MoveOut < @Start
                BEGIN ROLLBACK; SELECT 'BEFORE_START' AS Result, @Start AS StartDate; RETURN; END

                UPDATE TenantLeases
                SET IsActive = 0, TerminatedAt = GETDATE(), TerminationReason = @Reason,
                    BilledThrough = CASE WHEN @MoveOut < StartDate THEN DATEADD(DAY, -1, StartDate) ELSE @MoveOut END
                WHERE LeaseId = @LeaseId AND IsActive = 1;

                -- An earlier lease on the same unit (renewed, new term not started yet) must stop at move-out too
                DECLARE @Affected TABLE (LeaseId INT);
                UPDATE l SET BilledThrough = CASE WHEN @MoveOut < l.StartDate THEN DATEADD(DAY, -1, l.StartDate) ELSE @MoveOut END
                OUTPUT inserted.LeaseId INTO @Affected
                FROM TenantLeases l
                WHERE l.TenantId = @TenantId AND l.UnitId = @UnitId AND l.LeaseId <> @LeaseId
                  AND (l.BilledThrough IS NULL OR l.BilledThrough > @MoveOut);
                INSERT INTO @Affected SELECT @LeaseId WHERE @TenantId IS NOT NULL;

                -- The tenant moves out only when no other active lease remains; otherwise point the tenant row at that lease
                IF NOT EXISTS (SELECT 1 FROM TenantLeases WHERE TenantId = @TenantId AND IsActive = 1)
                    UPDATE Tenants SET IsActive = 0, MoveOutDate = @MoveOut WHERE TenantId = @TenantId;
                ELSE
                    UPDATE t SET UnitId = u.UnitId, FloorId = u.FloorId, BuildingId = u.BuildingId, MonthlyRent = a.RentAmount
                    FROM Tenants t
                    CROSS APPLY (SELECT TOP 1 UnitId, RentAmount FROM TenantLeases
                                 WHERE TenantId = t.TenantId AND IsActive = 1 ORDER BY StartDate DESC) a
                    JOIN Units u ON u.UnitId = a.UnitId
                    WHERE t.TenantId = @TenantId AND t.UnitId = @UnitId;
                " + AdjustInvoicesAfterEndSql + @"
                COMMIT;

                -- NO_RENT: ended before it started and no earlier term was cut either, so no rent is billed at all
                SELECT CASE WHEN @MoveOut < @Start AND NOT EXISTS (SELECT 1 FROM @Affected WHERE LeaseId <> @LeaseId)
                            THEN 'NO_RENT' ELSE 'OK' END AS Result, @Start AS StartDate;";
            var parameters = new[]
            {
                new SqlParameter("@LeaseId", req.LeaseId),
                new SqlParameter("@Reason", string.IsNullOrWhiteSpace(req.Reason) ? "Terminated" : req.Reason),
                new SqlParameter("@MoveOut", SqlDbType.Date) { Value = moveOutDate.Date },
                new SqlParameter("@UserId", userId),
            };
            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
            var result = dt.Rows.Count > 0 ? dt.Rows[0]["Result"].ToString() : null;
            return result switch
            {
                "OK" => new ApiResponse { IsSuccess = true, Message = $"Lease ended. Rent is billed through {moveOutDate:dd MMM yyyy}." },
                "NO_RENT" => new ApiResponse
                {
                    IsSuccess = true,
                    Message = $"Lease ended before its start date ({Convert.ToDateTime(dt.Rows[0]["StartDate"]):dd MMM yyyy}). No rent is billed.",
                },
                "BEFORE_START" => new ApiResponse
                {
                    IsSuccess = false,
                    ErrorMessage = $"The move-out date can't be before the lease start date ({Convert.ToDateTime(dt.Rows[0]["StartDate"]):dd MMM yyyy}).",
                },
                _ => new ApiResponse { IsSuccess = false, ErrorMessage = "Active lease not found." },
            };
        }

        // Cancels a lease made by mistake: it is marked 'Lease cancelled' and billed through the day before it
        // starts, so it is never billed, its rent invoices are cancelled and the unit is free again.
        // Refused when payments, discounts or waivers are recorded on its rent invoices (reverse those first), and for
        // a renewal (the tenant already lives there under the earlier term: use Cancel Renewal or End).
        public async Task<ApiResponse> CancelLeaseAsync(int leaseId, int userId)
        {
            string query = @"
                SET XACT_ABORT ON;
                DECLARE @TenantId INT, @UnitId INT, @Start DATE, @AdjReason NVARCHAR(300) = N'Lease cancelled';

                -- Lock the lease row like Renew/Terminate do: a second simultaneous cancel finds it no longer active
                BEGIN TRAN;
                SELECT @TenantId = TenantId, @UnitId = UnitId, @Start = StartDate
                FROM TenantLeases WITH (UPDLOCK, HOLDLOCK) WHERE LeaseId = @LeaseId AND IsActive = 1;
                IF @TenantId IS NULL BEGIN ROLLBACK; SELECT 'NOT_FOUND' AS Result; RETURN; END

                IF EXISTS (SELECT 1 FROM TenantLeases
                           WHERE TenantId = @TenantId AND UnitId = @UnitId AND LeaseId <> @LeaseId
                             AND TerminationReason = 'Renewed' AND BilledThrough = DATEADD(DAY, -1, @Start))
                BEGIN ROLLBACK; SELECT 'RENEWAL' AS Result; RETURN; END

                IF EXISTS (SELECT 1 FROM RentInvoices ri JOIN Payments p ON p.RentInvoiceId = ri.Id
                           WHERE ri.LeaseId = @LeaseId AND ri.ChargeType IS NULL)
                BEGIN ROLLBACK; SELECT 'HAS_PAYMENTS' AS Result; RETURN; END

                UPDATE TenantLeases
                SET IsActive = 0, TerminatedAt = GETDATE(), TerminationReason = 'Lease cancelled',
                    BilledThrough = DATEADD(DAY, -1, StartDate)
                WHERE LeaseId = @LeaseId;

                -- Same tenant update as End: inactive when no other active lease remains, otherwise point at that lease
                IF NOT EXISTS (SELECT 1 FROM TenantLeases WHERE TenantId = @TenantId AND IsActive = 1)
                    UPDATE Tenants SET IsActive = 0, MoveOutDate = CAST(GETDATE() AS DATE) WHERE TenantId = @TenantId;
                ELSE
                    UPDATE t SET UnitId = u.UnitId, FloorId = u.FloorId, BuildingId = u.BuildingId, MonthlyRent = a.RentAmount
                    FROM Tenants t
                    CROSS APPLY (SELECT TOP 1 UnitId, RentAmount FROM TenantLeases
                                 WHERE TenantId = t.TenantId AND IsActive = 1 ORDER BY StartDate DESC) a
                    JOIN Units u ON u.UnitId = a.UnitId
                    WHERE t.TenantId = @TenantId AND t.UnitId = @UnitId;

                -- Its rent invoices (none can have payments) are cancelled by the usual correction
                DECLARE @Affected TABLE (LeaseId INT);
                INSERT INTO @Affected VALUES (@LeaseId);
                " + AdjustInvoicesAfterEndSql + @"
                COMMIT;
                SELECT 'OK' AS Result;";

            var parameters = new[]
            {
                new SqlParameter("@LeaseId", leaseId),
                new SqlParameter("@UserId", userId),
            };
            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
            var result = dt.Rows.Count > 0 ? dt.Rows[0]["Result"].ToString() : null;
            return result switch
            {
                "OK" => new ApiResponse { IsSuccess = true, Message = "Lease cancelled. No rent is billed for it and the unit is free again." },
                "RENEWAL" => new ApiResponse { IsSuccess = false, ErrorMessage = "This lease is a renewal of an earlier term, so it can't be cancelled as a mistake. Use End to end the tenancy." },
                "HAS_PAYMENTS" => new ApiResponse { IsSuccess = false, ErrorMessage = "Payments, discounts or waivers are recorded on this lease's invoices. Reverse them first." },
                _ => new ApiResponse { IsSuccess = false, ErrorMessage = "Active lease not found." },
            };
        }
    }
}
