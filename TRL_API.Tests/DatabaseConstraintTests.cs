using Microsoft.Data.SqlClient;
using Xunit;

namespace TRL_API.Tests
{
    // The rules of migration 2026-10-12c_db_constraints: bad rows are refused by the database, while every row shape the
    // application writes (or wrote in older versions) is still accepted. Needs TRL_TEST_DB with at least one tenant,
    // unit, user, refresh token, cash payment and lease rent invoice (an E2E database kept with --keep has them).
    // Every statement runs in a transaction that is rolled back.
    public class DatabaseConstraintTests
    {
        private const string InsInv = "INSERT dbo.RentInvoices (TenantId, TotalRent, PendingAmount, InvoiceDate, DueDate, StatusId, ChargeType, LateFeePerDay, LateFeeMaxMultiplier) ";
        private const string InsPay = "INSERT dbo.Payments (TenantId, PaymentAmount, PaymentDate, RentInvoiceId, PaymentMethod, DiscountAmount, DiscountPercent, IsLateFeeWaived) ";
        private const string Ids = @"
            DECLARE @T INT = (SELECT MIN(TenantId) FROM dbo.Tenants), @U INT = (SELECT MIN(UnitId) FROM dbo.Units),
                    @User INT = (SELECT MIN(UserId) FROM dbo.Users),
                    @Inv INT = (SELECT MIN(Id) FROM dbo.RentInvoices WHERE StatusId <> 6),
                    @Pay INT = (SELECT MIN(Id) FROM dbo.Payments WHERE ReversalOfPaymentId IS NULL AND PaymentAmount > 0
                                AND NOT EXISTS (SELECT 1 FROM dbo.Payments r WHERE r.ReversalOfPaymentId = Payments.Id)),
                    @Rent INT = (SELECT MIN(Id) FROM dbo.RentInvoices WHERE ChargeType IS NULL AND StatusId <> 6 AND LeaseId IS NOT NULL);
            IF @T IS NULL OR @U IS NULL OR @User IS NULL OR @Inv IS NULL OR @Pay IS NULL OR @Rent IS NULL
                OR NOT EXISTS (SELECT 1 FROM dbo.RefreshTokens) THROW 50099, 'TEST DATA MISSING', 1;
            ";

        // null = accepted; otherwise the error message
        private static async Task<string?> Try(string sql)
        {
            await using var con = new SqlConnection(TestDb.ConnectionString);
            await con.OpenAsync();
            await using var tx = con.BeginTransaction();
            try
            {
                await using var cmd = new SqlCommand(Ids + sql, con, tx);
                await cmd.ExecuteNonQueryAsync();
                return null;
            }
            catch (SqlException e)
            {
                if (e.Message.Contains("TEST DATA MISSING")) throw;
                return e.Message;
            }
            finally { if (tx.Connection != null) tx.Rollback(); }
        }

        public static TheoryData<string, string, string> Refused => new()
        {
            { "negative rent", InsInv + "VALUES (@T, -1, 0, '2026-10-01', '2026-10-05', 2, N'Other', 500, 2)", "CK_RentInvoices_Amounts" },
            { "negative charged late fee", "UPDATE dbo.RentInvoices SET LateFeeCharged = -1 WHERE Id = @Inv", "CK_RentInvoices_Amounts" },
            { "negative late fee per day", InsInv + "VALUES (@T, 100, 100, '2026-10-01', '2026-10-05', 2, N'Other', -1, 2)", "CK_RentInvoices_LateFeeTerms" },
            { "unknown charge type", InsInv + "VALUES (@T, 100, 100, '2026-10-01', '2026-10-05', 2, N'Parking', 500, 2)", "CK_RentInvoices_ChargeType" },
            { "invoice without status", InsInv + "VALUES (@T, 100, 100, '2026-10-01', '2026-10-05', NULL, N'Other', 500, 2)", "StatusId" },
            { "unknown status", InsInv + "VALUES (@T, 100, 100, '2026-10-01', '2026-10-05', 99, N'Other', 500, 2)", "FK_RentInvoices_Status" },
            { "unknown tenant", InsInv + "VALUES (999999, 100, 100, '2026-10-01', '2026-10-05', 2, N'Other', 500, 2)", "FK_RentInvoices_Tenant" },
            { "audit row of a missing invoice", "INSERT dbo.InvoiceAudit (InvoiceId, Action) VALUES (999999, 'TEST')", "FK_InvoiceAudit_Invoice" },
            { "unknown payment method", InsPay + "VALUES (@T, 10, GETDATE(), @Inv, 'JazzCash', 0, 0, 0)", "CK_Payments_Method" },
            { "lease with rent 0", "INSERT dbo.TenantLeases (TenantId, UnitId, RentAmount, StartDate, TenureMonths, IsActive) VALUES (@T, @U, 0, '2026-10-01', 12, 0)", "CK_TenantLeases_Terms" },
            { "lease of 0 months", "INSERT dbo.TenantLeases (TenantId, UnitId, RentAmount, StartDate, TenureMonths, IsActive) VALUES (@T, @U, 1000, '2026-10-01', 0, 0)", "CK_TenantLeases_Terms" },
            { "duplicate refresh token", "INSERT dbo.RefreshTokens (Token, Expires, UserId) SELECT TOP 1 Token, DATEADD(DAY, 1, SYSUTCDATETIME()), @User FROM dbo.RefreshTokens", "UX_RefreshTokens_Token" },
            { "second rent invoice for a lease month", "INSERT dbo.RentInvoices (TenantId, LeaseId, TotalRent, PendingAmount, InvoiceDate, DueDate, StatusId, LateFeePerDay, LateFeeMaxMultiplier) SELECT TenantId, LeaseId, 1, 1, InvoiceDate, DueDate, 2, 500, 2 FROM dbo.RentInvoices WHERE Id = @Rent", "UX_RentInvoices_RentPerLeaseMonth" },
            { "reversal row that adds money", "INSERT dbo.Payments (TenantId, PaymentAmount, PaymentDate, RentInvoiceId, PaymentMethod, DiscountAmount, DiscountPercent, IsLateFeeWaived, ReversalOfPaymentId) SELECT TenantId, 5, GETDATE(), RentInvoiceId, PaymentMethod, 0, 0, 0, Id FROM dbo.Payments WHERE Id = @Pay", "CK_Payments_Reversal" },
        };

        public static TheoryData<string, string> Accepted => new()
        {
            { "reversal row (negative, original method)", "INSERT dbo.Payments (TenantId, PaymentAmount, PaymentDate, RentInvoiceId, PaymentMethod, DiscountAmount, DiscountPercent, IsLateFeeWaived, ReversalOfPaymentId) SELECT TenantId, -PaymentAmount, GETDATE(), RentInvoiceId, PaymentMethod, -DiscountAmount, 0, 0, Id FROM dbo.Payments WHERE Id = @Pay" },
            { "old adjustment: negative amount with method Cash", InsPay + "VALUES (@T, -10, GETDATE(), @Inv, 'Cash', 0, 0, 0)" },
            { "old adjustment: negative discount", InsPay + "VALUES (@T, 0, GETDATE(), @Inv, 'Adjustment', -10, 0, 0)" },
            { "old discount/waiver-only row without method", InsPay + "VALUES (@T, 0, GETDATE(), @Inv, NULL, 10, 0, 1)" },
            { "old discount % typed above 100", InsPay + "VALUES (@T, 0, GETDATE(), @Inv, 'Adjustment', 10, 150, 0)" },
            { "settlement Credit to Deposit (negative)", InsPay + "VALUES (@T, -10, GETDATE(), @Inv, 'Credit to Deposit', 0, 0, 0)" },
            { "settlement Security Deposit payment", InsPay + "VALUES (@T, 10, GETDATE(), @Inv, 'Security Deposit', 0, 0, 0)" },
            { "settlement deduction invoice (Cleaning, no late fee)", InsInv + "VALUES (@T, 100, 100, '2026-10-01', '2026-10-01', 2, N'Cleaning', 0, 2)" },
            { "old charge type Late Fine", InsInv + "VALUES (@T, 100, 100, '2026-10-01', '2026-10-05', 2, N'Late Fine', 500, 2)" },
            { "old charge type Security Deposit", InsInv + "VALUES (@T, 100, 100, '2026-10-01', '2026-10-05', 2, N'Security Deposit', 500, 2)" },
            { "rent invoice of 0 (no covered days)", InsInv + "VALUES (@T, 0, 0, '2026-10-01', '2026-10-05', 2, NULL, 500, 2)" },
            { "cancelled invoice", "UPDATE dbo.RentInvoices SET StatusId = 6 WHERE Id = @Inv" },
            { "negative cached balance (old data)", "UPDATE dbo.RentInvoices SET PendingAmount = -5 WHERE Id = @Inv" },
        };

        [DbTheory]
        [MemberData(nameof(Refused))]
        public async Task Database_RefusesBadRow(string name, string sql, string expected)
        {
            var error = await Try(sql);
            Assert.True(error != null, $"{name}: was accepted");
            Assert.Contains(expected, error);
        }

        [DbTheory]
        [MemberData(nameof(Accepted))]
        public async Task Database_AcceptsRowShapeTheApplicationWrites(string name, string sql)
        {
            var error = await Try(sql);
            Assert.True(error == null, $"{name}: {error}");
        }
    }

    public sealed class DbTheoryAttribute : TheoryAttribute
    {
        public DbTheoryAttribute()
        {
            if (TestDb.ConnectionString == null)
                Skip = "Set TRL_TEST_DB to a connection string for a *_tmp database copy to run database tests.";
        }
    }
}
