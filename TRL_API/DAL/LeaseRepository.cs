using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.Data;
using TRL_API.Models;

namespace TRL_API.DAL
{
    public class LeaseRepository
    {
        private readonly DbHelper _dbHelper;
        public LeaseRepository(DbHelper dbHelper) => _dbHelper = dbHelper;

        public async Task<DataTable> GetAllAsync()
        {
            string query = @"
                SELECT tl.LeaseId, tl.TenantId, t.Name AS TenantName, tl.UnitId, u.UnitNumber,
                       b.BuildingName, f.FloorNumber, tl.RentAmount, tl.StartDate, tl.EndDate,
                       tl.TenureMonths, tl.IsActive,
                       CASE WHEN tl.IsActive = 1 AND tl.EndDate < CAST(GETDATE() AS DATE) THEN 1 ELSE 0 END AS IsExpired
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

        public async Task<ApiResponse> CreateAsync(Lease lease, int userId)
        {
            string query = @"
                INSERT INTO TenantLeases (TenantId, UnitId, RentAmount, StartDate, TenureMonths, IsActive, CreatedBy)
                VALUES (@TenantId, @UnitId, @RentAmount, @StartDate, @TenureMonths, 1, @CreatedBy);";

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

        // Ends the current lease and starts a fresh one on the same unit —
        // keeps full history instead of overwriting rent/tenure in place.
        public async Task<ApiResponse> RenewAsync(RenewLeaseRequest req, int userId)
        {
            string query = @"
                DECLARE @TenantId INT, @UnitId INT, @OldRent DECIMAL(18,2);
                SELECT @TenantId = TenantId, @UnitId = UnitId, @OldRent = RentAmount
                FROM TenantLeases WHERE LeaseId = @LeaseId AND IsActive = 1;

                IF @TenantId IS NULL
                BEGIN
                    SELECT 'NOT_FOUND' AS Result; RETURN;
                END

                UPDATE TenantLeases SET IsActive = 0, TerminatedAt = GETDATE(),
                       TerminationReason = 'Renewed' WHERE LeaseId = @LeaseId;

                INSERT INTO TenantLeases (TenantId, UnitId, RentAmount, StartDate, TenureMonths, IsActive, CreatedBy)
                VALUES (@TenantId, @UnitId, ISNULL(@NewRent, @OldRent), CAST(GETDATE() AS DATE), @TenureMonths, 1, @CreatedBy);

                SELECT 'OK' AS Result;";

            var parameters = new[]
            {
                new SqlParameter("@LeaseId", req.LeaseId),
                new SqlParameter("@NewRent", (object?)req.NewRentAmount ?? DBNull.Value),
                new SqlParameter("@TenureMonths", req.TenureMonths),
                new SqlParameter("@CreatedBy", userId),
            };
            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(query, parameters);
            return dt.Rows.Count > 0 && dt.Rows[0]["Result"].ToString() == "OK"
                ? new ApiResponse { IsSuccess = true }
                : new ApiResponse { IsSuccess = false, ErrorMessage = "Active lease not found." };
        }

        public async Task<ApiResponse> TerminateAsync(TerminateLeaseRequest req)
        {
            string query = @"
                UPDATE TenantLeases
                SET IsActive = 0, TerminatedAt = GETDATE(), TerminationReason = @Reason
                WHERE LeaseId = @LeaseId AND IsActive = 1;";
            var parameters = new[]
            {
                new SqlParameter("@LeaseId", req.LeaseId),
                new SqlParameter("@Reason", string.IsNullOrWhiteSpace(req.Reason) ? "Terminated" : req.Reason),
            };
            return await _dbHelper.ExecuteQueryAsync(query, parameters);
        }
    }
}
