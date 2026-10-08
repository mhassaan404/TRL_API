using TRL_API.BLL;
using TRL_API.Models;
using Xunit;

namespace TRL_API.Tests
{
    // Tenant Statement: opening balance, period lines, running and closing balance (ReportService.BuildStatement)
    public class TenantStatementTests
    {
        private static StatementLine L(string date, string type, decimal charge, decimal credit) =>
            new() { Date = DateTime.Parse(date), Type = type, Charge = charge, Credit = credit };

        private static TenantStatement Build(string from, string to, params StatementLine[] lines)
        {
            var s = new TenantStatement();
            ReportService.BuildStatement(s, lines, DateTime.Parse(from), DateTime.Parse(to));
            return s;
        }

        private static readonly StatementLine[] History =
        {
            L("2026-08-01", "Invoice", 10000, 0),
            L("2026-08-05", "Payment", 0, 6000),
            L("2026-08-05", "Discount", 0, 500),
            L("2026-09-01", "Invoice", 10000, 0),
            L("2026-09-10", "Late Fee", 1500, 0),
            L("2026-09-30", "Payment", 0, 12000),
            L("2026-10-01", "Invoice", 10000, 0),
        };

        [Fact]
        public void Entries_before_From_make_the_opening_balance()
        {
            var s = Build("2026-09-01", "2026-09-30", History);
            Assert.Equal(3500, s.OpeningBalance);
            Assert.Equal(3, s.Lines.Count);
            Assert.Equal(11500, s.TotalCharges);
            Assert.Equal(12000, s.TotalCredits);
            Assert.Equal(3000, s.ClosingBalance);
            Assert.Equal(new decimal[] { 13500, 15000, 3000 }, s.Lines.Select(l => l.Balance));
        }

        [Fact]
        public void Whole_history_starts_at_zero_and_ignores_entries_after_To()
        {
            var s = Build("2000-01-01", "2026-09-30", History);
            Assert.Equal(0, s.OpeningBalance);
            Assert.Equal(6, s.Lines.Count);
            Assert.Equal(3000, s.ClosingBalance);
            Assert.Equal(s.ClosingBalance, s.Lines[^1].Balance);
        }

        [Fact]
        public void Single_day_includes_that_day()
        {
            var s = Build("2026-08-05", "2026-08-05", History);
            Assert.Equal(10000, s.OpeningBalance);
            Assert.Equal(2, s.Lines.Count);
            Assert.Equal(3500, s.ClosingBalance);
        }

        [Fact]
        public void Period_with_no_entries_keeps_the_opening_balance()
        {
            var s = Build("2026-09-11", "2026-09-29", History);
            Assert.Empty(s.Lines);
            Assert.Equal(15000, s.OpeningBalance);
            Assert.Equal(15000, s.ClosingBalance);
        }

        [Fact]
        public void Overpayment_gives_a_negative_balance_credit()
        {
            var s = Build("2026-01-01", "2026-12-31", L("2026-02-01", "Invoice", 5000, 0), L("2026-02-02", "Payment", 0, 6000));
            Assert.Equal(-1000, s.ClosingBalance);
        }

        [Fact]
        public void No_entries_at_all()
        {
            var s = Build("2026-01-01", "2026-12-31");
            Assert.Equal(0, s.OpeningBalance);
            Assert.Equal(0, s.ClosingBalance);
            Assert.Empty(s.Lines);
        }

        [Fact]
        public void Statement_range_has_no_length_limit_but_checks_order_and_years()
        {
            var svc = new ReportService(null!);
            Assert.Null(svc.ValidateStatementRange(new DateTime(2000, 1, 1), new DateTime(2026, 10, 8)));
            Assert.Equal("From date must be on or before To date.", svc.ValidateStatementRange(new DateTime(2026, 10, 9), new DateTime(2026, 10, 8)));
            Assert.Equal("Please choose a From and To date.", svc.ValidateStatementRange(null, new DateTime(2026, 10, 8)));
            Assert.Equal("Please choose dates between 2000 and 2100.", svc.ValidateStatementRange(new DateTime(1999, 1, 1), new DateTime(2026, 10, 8)));
        }
    }
}
