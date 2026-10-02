using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.Data;
using TRL_API.Models;

namespace TRL_API.DAL
{
    public class LateFeeSettingsRepository : ILateFeeSettingsRepository
    {
        private readonly DbHelper _dbHelper;
        public LateFeeSettingsRepository(DbHelper dbHelper) => _dbHelper = dbHelper;

        // The single settings row (seeded by migration 2026-10-02_late_fee_settings.sql). A missing row is a
        // database setup error, never silently replaced by built-in values.
        public async Task<LateFeeSettings> GetAsync()
        {
            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT s.PaymentDueDays, s.LateFeePerDay, s.MaxLateFeeMultiplier, u.Username AS UpdatedBy, s.UpdatedAt
                FROM LateFeeSettings s
                LEFT JOIN Users u ON u.UserId = s.UpdatedBy
                WHERE s.Id = 1;");
            if (dt.Rows.Count == 0)
                throw new InvalidOperationException("Late fee settings are missing. Apply migration 2026-10-02_late_fee_settings.sql.");

            var r = dt.Rows[0];
            return new LateFeeSettings
            {
                PaymentDueDays = Convert.ToInt32(r["PaymentDueDays"]),
                LateFeePerDay = Convert.ToDecimal(r["LateFeePerDay"]),
                MaxLateFeeMultiplier = Convert.ToDecimal(r["MaxLateFeeMultiplier"]),
                UpdatedBy = r["UpdatedBy"] == DBNull.Value ? null : r["UpdatedBy"].ToString(),
                UpdatedAt = r["UpdatedAt"] == DBNull.Value ? null : Convert.ToDateTime(r["UpdatedAt"]),
            };
        }

        public async Task<ApiResponse> SaveAsync(int paymentDueDays, decimal lateFeePerDay, decimal maxLateFeeMultiplier, int userId)
        {
            return await _dbHelper.ExecuteQueryAsync(@"
                UPDATE LateFeeSettings
                SET PaymentDueDays = @PaymentDueDays, LateFeePerDay = @LateFeePerDay,
                    MaxLateFeeMultiplier = @MaxLateFeeMultiplier, UpdatedBy = @UserId, UpdatedAt = GETDATE()
                WHERE Id = 1;",
                new[]
                {
                    new SqlParameter("@PaymentDueDays", paymentDueDays),
                    new SqlParameter("@LateFeePerDay", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = lateFeePerDay },
                    new SqlParameter("@MaxLateFeeMultiplier", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = maxLateFeeMultiplier },
                    new SqlParameter("@UserId", userId),
                });
        }
    }
}
