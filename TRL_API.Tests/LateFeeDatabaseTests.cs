using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using TRL_API.Data;
using TRL_API.DAL;
using Xunit;

namespace TRL_API.Tests
{
    // Runs only against a disposable database copy: set TRL_TEST_DB to a connection string whose database name
    // ends in "_tmp" (e.g. a COPY_ONLY backup of TRL_DB restored as TRL_Test_tmp, with the migrations applied).
    // Never point it at the real database. Without it these tests are skipped.
    public sealed class DbFactAttribute : FactAttribute
    {
        public DbFactAttribute()
        {
            if (TestDb.ConnectionString == null)
                Skip = "Set TRL_TEST_DB to a connection string for a *_tmp database copy to run database tests.";
        }
    }

    public static class TestDb
    {
        public static string? ConnectionString
        {
            get
            {
                var cs = Environment.GetEnvironmentVariable("TRL_TEST_DB");
                if (string.IsNullOrWhiteSpace(cs)) return null;
                var db = new SqlConnectionStringBuilder(cs).InitialCatalog;
                return db.EndsWith("_tmp", StringComparison.OrdinalIgnoreCase) ? cs : null;
            }
        }
    }

    public class LateFeeDatabaseTests
    {
        // Runs the statements in a transaction that is always rolled back, so nothing is left behind
        private static async Task<T> InRollback<T>(Func<SqlConnection, SqlTransaction, Task<T>> body)
        {
            await using var con = new SqlConnection(TestDb.ConnectionString);
            await con.OpenAsync();
            await using var tx = con.BeginTransaction();
            try { return await body(con, tx); }
            finally { tx.Rollback(); }
        }

        private static async Task<object?> Scalar(SqlConnection con, SqlTransaction tx, string sql, params SqlParameter[] p)
        {
            await using var cmd = new SqlCommand(sql, con, tx);
            cmd.Parameters.AddRange(p);
            return await cmd.ExecuteScalarAsync();
        }

        private static async Task<decimal> Fee(int daysOverdue, decimal rent, decimal perDay, decimal max) =>
            await InRollback(async (con, tx) => Convert.ToDecimal(await Scalar(con, tx,
                "SELECT dbo.CalculateLateFee(@Rent, @Rent, DATEADD(DAY, -@Days, CAST(GETUTCDATE() AS DATE)), GETUTCDATE(), @PerDay, @Max)",
                new SqlParameter("@Rent", rent), new SqlParameter("@Days", daysOverdue),
                new SqlParameter("@PerDay", perDay), new SqlParameter("@Max", max))));

        [DbFact]
        public async Task No_late_fee_on_or_before_the_due_date()
        {
            Assert.Equal(0m, await Fee(0, 30000, 500, 2));
            Assert.Equal(0m, await Fee(-3, 30000, 500, 2));
        }

        [DbFact]
        public async Task Late_fee_starts_the_day_after_the_due_date_at_the_daily_rate()
        {
            Assert.Equal(500m, await Fee(1, 30000, 500, 2));
            Assert.Equal(2000m, await Fee(4, 30000, 500, 2));
            Assert.Equal(7500m, await Fee(10, 30000, 750, 2));
        }

        [DbFact]
        public async Task Late_fee_stops_at_the_maximum_multiple_of_the_invoice_rent()
        {
            Assert.Equal(60000m, await Fee(120, 30000, 500, 2));  // exactly at the cap
            Assert.Equal(60000m, await Fee(400, 30000, 500, 2));  // capped
            Assert.Equal(45000m, await Fee(100, 30000, 1000, 1.5m));
            Assert.Equal(3000m, await Fee(10, 2000, 500, 1.5m));
        }

        [DbFact]
        public async Task Open_late_fee_uses_the_rule_stored_on_the_invoice_not_the_current_settings()
        {
            var (before, after) = await InRollback(async (con, tx) =>
            {
                var id = Convert.ToInt32(await Scalar(con, tx, @"
                    INSERT INTO RentInvoices (TenantId, TotalRent, PendingAmount, OverPaidAmount, InvoiceDate, DueDate, StatusId,
                                              CreatedAt, LateFeePerDay, LateFeeMaxMultiplier)
                    VALUES ((SELECT TOP 1 TenantId FROM Tenants), 10000, 10000, 0, DATEADD(DAY, -15, CAST(GETUTCDATE() AS DATE)),
                            DATEADD(DAY, -10, CAST(GETUTCDATE() AS DATE)), 2, GETDATE(), 300, 2);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);"));
                const string open = "SELECT OpenLateFee FROM dbo.InvoiceBalance(@Id, 0)";
                var b = Convert.ToDecimal(await Scalar(con, tx, open, new SqlParameter("@Id", id)));

                // Changing the settings must not re-price the existing invoice
                await Scalar(con, tx, "UPDATE LateFeeSettings SET LateFeePerDay = 5000, MaxLateFeeMultiplier = 0.5, PaymentDueDays = 0 WHERE Id = 1");
                var a = Convert.ToDecimal(await Scalar(con, tx, open, new SqlParameter("@Id", id)));
                return (b, a);
            });

            Assert.Equal(3000m, before); // 10 days x 300
            Assert.Equal(before, after);
        }

        [DbFact]
        public async Task Waived_charged_or_paid_invoices_have_no_open_late_fee()
        {
            var fees = await InRollback(async (con, tx) =>
            {
                async Task<int> Invoice(decimal lateFeeCharged) => Convert.ToInt32(await Scalar(con, tx, @"
                    INSERT INTO RentInvoices (TenantId, TotalRent, PendingAmount, OverPaidAmount, InvoiceDate, DueDate, StatusId,
                                              CreatedAt, LateFeePerDay, LateFeeMaxMultiplier, LateFeeCharged)
                    VALUES ((SELECT TOP 1 TenantId FROM Tenants), 10000, 10000, 0, DATEADD(DAY, -15, CAST(GETUTCDATE() AS DATE)),
                            DATEADD(DAY, -10, CAST(GETUTCDATE() AS DATE)), 2, GETDATE(), 500, 2, @Charged);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);", new SqlParameter("@Charged", lateFeeCharged)));
                async Task Pay(int id, decimal amount, bool waive) => await Scalar(con, tx, @"
                    INSERT INTO Payments (RentInvoiceId, TenantId, PaymentAmount, PaymentDate, PaymentMethod, DiscountAmount, DiscountPercent, IsLateFeeWaived)
                    VALUES (@Id, (SELECT TenantId FROM RentInvoices WHERE Id = @Id), @Amt, GETDATE(), 'Cash', 0, 0, @Waive)",
                    new SqlParameter("@Id", id), new SqlParameter("@Amt", amount), new SqlParameter("@Waive", waive));
                async Task<decimal> Open(int id) => Convert.ToDecimal(await Scalar(con, tx,
                    "SELECT OpenLateFee FROM dbo.InvoiceBalance(@Id, 0)", new SqlParameter("@Id", id)));

                var unpaid = await Invoice(0);
                var waived = await Invoice(0); await Pay(waived, 1000, waive: true);
                var charged = await Invoice(5000);
                var paid = await Invoice(0); await Pay(paid, 10000, waive: false);
                return (await Open(unpaid), await Open(waived), await Open(charged), await Open(paid));
            });

            Assert.Equal(5000m, fees.Item1);
            Assert.Equal(0m, fees.Item2);
            Assert.Equal(0m, fees.Item3);
            Assert.Equal(0m, fees.Item4);
        }

        [DbFact]
        public async Task Database_rejects_invalid_settings_and_invoices_without_a_late_fee_rule()
        {
            foreach (var bad in new[]
            {
                "UPDATE LateFeeSettings SET PaymentDueDays = 91 WHERE Id = 1",
                "UPDATE LateFeeSettings SET LateFeePerDay = 0 WHERE Id = 1",
                "UPDATE LateFeeSettings SET MaxLateFeeMultiplier = 12.5 WHERE Id = 1",
                "INSERT INTO LateFeeSettings (Id, PaymentDueDays, LateFeePerDay, MaxLateFeeMultiplier) VALUES (2, 5, 500, 2)",
                @"INSERT INTO RentInvoices (TenantId, TotalRent, PendingAmount, OverPaidAmount, InvoiceDate, DueDate, StatusId, CreatedAt)
                  VALUES ((SELECT TOP 1 TenantId FROM Tenants), 1, 1, 0, GETDATE(), GETDATE(), 2, GETDATE())",
            })
            {
                await Assert.ThrowsAsync<SqlException>(() => InRollback(async (con, tx) => await Scalar(con, tx, bad)));
            }
        }

        [DbFact]
        public async Task Repository_saves_and_reads_the_settings()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString })
                .Build();
            var repo = new LateFeeSettingsRepository(new DbHelper(config));
            var original = await repo.GetAsync();
            var userId = await InRollback(async (con, tx) => Convert.ToInt32(await Scalar(con, tx, "SELECT TOP 1 UserId FROM Users ORDER BY UserId")));
            try
            {
                var res = await repo.SaveAsync(9, 650, 1.5m, userId);
                var saved = await repo.GetAsync();

                Assert.True(res.IsSuccess);
                Assert.Equal(9, saved.PaymentDueDays);
                Assert.Equal(650m, saved.LateFeePerDay);
                Assert.Equal(1.5m, saved.MaxLateFeeMultiplier);
                Assert.NotNull(saved.UpdatedAt);
                Assert.False(string.IsNullOrEmpty(saved.UpdatedBy));
            }
            finally
            {
                await repo.SaveAsync(original.PaymentDueDays, original.LateFeePerDay, original.MaxLateFeeMultiplier, userId);
            }
        }
    }
}
