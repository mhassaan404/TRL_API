using System.Data;
using NSubstitute;
using TRL_API.BLL;
using TRL_API.DAL;
using TRL_API.Models;
using Xunit;

namespace TRL_API.Tests
{
    // Invoice generation uses the stored settings: due date = invoice date + Payment Due Days, and every new
    // invoice keeps the late-fee rate and cap that were in force when it was created.
    public class RentServiceDueDateTests
    {
        private static readonly DateTime ThisMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);

        private static DataTable LeaseCharges(params (int tenantId, int leaseId, DateTime from, decimal amount)[] rows)
        {
            var dt = new DataTable();
            foreach (var c in new[] { "TenantId", "LeaseId", "UnitId" }) dt.Columns.Add(c, typeof(int));
            dt.Columns.Add("Amount", typeof(decimal));
            dt.Columns.Add("FromDate", typeof(DateTime));
            dt.Columns.Add("Descr", typeof(string));
            dt.Columns.Add("AlreadyInvoiced", typeof(bool));
            foreach (var r in rows) dt.Rows.Add(r.tenantId, r.leaseId, 100 + r.leaseId, r.amount, r.from, DBNull.Value, false);
            return dt;
        }

        private static (RentService svc, IRentRepository rent) Create(LateFeeSettings settings, DataTable charges)
        {
            var rent = Substitute.For<IRentRepository>();
            rent.GetLeaseChargesForMonth(default, default).ReturnsForAnyArgs(charges);
            rent.CreateInvoice(default, default, default, default, default, default).ReturnsForAnyArgs(new ApiResponse { IsSuccess = true, RowsAffected = 1 });
            var lateFee = Substitute.For<ILateFeeSettingsRepository>();
            lateFee.GetAsync().Returns(settings);
            return (new RentService(rent, lateFee), rent);
        }

        private static LateFeeSettings Settings(int due, decimal perDay, decimal max) =>
            new() { PaymentDueDays = due, LateFeePerDay = perDay, MaxLateFeeMultiplier = max };

        [Fact]
        public async Task Generated_rent_uses_the_due_days_setting_and_keeps_the_rate_and_cap()
        {
            var (svc, rent) = Create(Settings(7, 750, 1.5m), LeaseCharges((1, 10, ThisMonth, 30000)));

            var res = await svc.GenerateInvoicesAsync(ThisMonth.Month, ThisMonth.Year, null, null);

            Assert.True(res.IsSuccess);
            await rent.Received(1).CreateInvoice(1, 30000m, ThisMonth, ThisMonth.AddDays(7), 750m, 1.5m,
                null, null, 10, 110);
        }

        [Fact]
        public async Task Mid_month_move_in_is_due_the_setting_days_after_move_in()
        {
            var moveIn = ThisMonth.AddDays(14);
            var (svc, rent) = Create(Settings(5, 500, 2), LeaseCharges((2, 20, moveIn, 16000)));

            await svc.GenerateInvoicesAsync(ThisMonth.Month, ThisMonth.Year, null, null);

            await rent.Received(1).CreateInvoice(2, 16000m, moveIn, moveIn.AddDays(5), 500m, 2m, null, null, 20, 120);
        }

        [Fact]
        public async Task Default_settings_give_the_same_invoice_as_before()
        {
            // 5 days / 500 / 2 = the rule that was hard-coded before the settings existed
            var (svc, rent) = Create(Settings(5, 500, 2), LeaseCharges((3, 30, ThisMonth, 40000)));

            await svc.GenerateInvoicesAsync(ThisMonth.Month, ThisMonth.Year, null, null);

            await rent.Received(1).CreateInvoice(3, 40000m, ThisMonth, ThisMonth.AddDays(5), 500m, 2m, null, null, 30, 130);
        }

        [Fact]
        public async Task An_explicit_due_days_value_still_overrides_the_setting()
        {
            var (svc, rent) = Create(Settings(7, 500, 2), LeaseCharges((1, 10, ThisMonth, 30000)));

            await svc.GenerateInvoicesAsync(ThisMonth.Month, ThisMonth.Year, 3, null);

            await rent.Received(1).CreateInvoice(1, 30000m, ThisMonth, ThisMonth.AddDays(3), 500m, 2m, null, null, 10, 110);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(91)]
        public async Task An_out_of_range_explicit_due_days_value_is_rejected(int days)
        {
            var (svc, rent) = Create(Settings(5, 500, 2), LeaseCharges((1, 10, ThisMonth, 30000)));

            var res = await svc.GenerateInvoicesAsync(ThisMonth.Month, ThisMonth.Year, days, null);

            Assert.False(res.IsSuccess);
            await rent.DidNotReceiveWithAnyArgs().CreateInvoice(default, default, default, default, default, default);
        }

        private static DataTable Created(params int[] ids)
        {
            var dt = new DataTable();
            dt.Columns.Add("Result", typeof(string));
            dt.Columns.Add("Info", typeof(int));
            foreach (var id in ids) dt.Rows.Add("OK", id);
            return dt;
        }

        [Fact]
        public async Task Extra_charges_default_to_today_and_the_due_days_setting_and_keep_the_rate_and_cap()
        {
            var (svc, rent) = Create(Settings(10, 300, 1), LeaseCharges());
            rent.CreateExtraChargesAsync(default!, default, default, default!, default, default, default, default, default).ReturnsForAnyArgs(Created(71, 72));

            var res = await svc.CreateExtraChargeAsync(new ExtraChargeRequest { TenantIds = new() { 5, 6, 5 }, ChargeType = "Maintenance", Description = " Paint ", Amount = 2500 });

            Assert.True(res.IsSuccess);
            Assert.Equal(2, res.RowsAffected);
            await rent.Received(1).CreateExtraChargesAsync(
                Arg.Is<IReadOnlyList<int>>(t => t.SequenceEqual(new[] { 5, 6 })), DateTime.Today, DateTime.Today.AddDays(10),
                "Maintenance", "Paint", 2500m, null, 300m, 1m);
        }

        [Fact]
        public async Task Extra_charge_without_late_fee_stores_a_zero_daily_rate_and_uses_the_given_date_and_due_days()
        {
            var (svc, rent) = Create(Settings(10, 300, 1), LeaseCharges());
            rent.CreateExtraChargesAsync(default!, default, default, default!, default, default, default, default, default).ReturnsForAnyArgs(Created(80));
            var date = DateTime.Today.AddDays(-3);

            var res = await svc.CreateExtraChargeAsync(new ExtraChargeRequest
            {
                TenantIds = new() { 9 }, ChargeType = "Rent Correction", Description = "Rent 01-04 Oct", Amount = 4000,
                ChargeDate = date, DueInDays = 2, RelatedInvoiceId = 33, ApplyLateFee = false,
            });

            Assert.True(res.IsSuccess);
            Assert.Equal(80, res.Id);
            await rent.Received(1).CreateExtraChargesAsync(
                Arg.Is<IReadOnlyList<int>>(t => t.SequenceEqual(new[] { 9 })), date, date.AddDays(2),
                "Rent Correction", "Rent 01-04 Oct", 4000m, 33, 0m, 1m);
        }

        public static IEnumerable<object[]> InvalidExtraCharges() => new[]
        {
            new object[] { new ExtraChargeRequest { TenantIds = new(), ChargeType = "Maintenance", Amount = 1 }, "Select at least one tenant." },
            new object[] { new ExtraChargeRequest { TenantIds = new() { 0 }, ChargeType = "Maintenance", Amount = 1 }, "One or more selected tenants are invalid." },
            new object[] { new ExtraChargeRequest { TenantIds = new() { 1 }, ChargeType = "Security Deposit", Amount = 1 }, "Charge type must be one of: Maintenance, Utility, Damage, Rent Correction, Other." },
            new object[] { new ExtraChargeRequest { TenantIds = new() { 1 }, ChargeType = "Maintenance", Amount = 0 }, "Amount must be greater than 0 and at most 10,000,000." },
            new object[] { new ExtraChargeRequest { TenantIds = new() { 1 }, ChargeType = "Maintenance", Amount = 1.005m }, "Amount can have at most 2 decimal places." },
            new object[] { new ExtraChargeRequest { TenantIds = new() { 1 }, ChargeType = "Rent Correction", Amount = 1 }, "Please describe the Rent Correction charge." },
            new object[] { new ExtraChargeRequest { TenantIds = new() { 1 }, ChargeType = "Other", Amount = 1, Description = "  " }, "Please describe the Other charge." },
            new object[] { new ExtraChargeRequest { TenantIds = new() { 1 }, ChargeType = "Maintenance", Amount = 1, DueInDays = 91 }, "Due days must be between 0 and 90." },
            new object[] { new ExtraChargeRequest { TenantIds = new() { 1 }, ChargeType = "Maintenance", Amount = 1, ChargeDate = DateTime.Today.AddMonths(2) }, "Charge date must be within the last 12 months or the next month." },
            new object[] { new ExtraChargeRequest { TenantIds = new() { 1, 2 }, ChargeType = "Rent Correction", Description = "x", Amount = 1, RelatedInvoiceId = 5 }, "A related invoice can only be set when charging one tenant." },
            new object[] { new ExtraChargeRequest { TenantIds = new() { 1 }, ChargeType = "Maintenance", Amount = 1, Description = new string('x', 256) }, "Description can be at most 255 characters." },
        };

        [Theory]
        [MemberData(nameof(InvalidExtraCharges))]
        public async Task Invalid_extra_charges_are_rejected_before_the_database(ExtraChargeRequest req, string message)
        {
            var (svc, rent) = Create(Settings(5, 500, 2), LeaseCharges());

            var res = await svc.CreateExtraChargeAsync(req);

            Assert.False(res.IsSuccess);
            Assert.Equal(message, res.ErrorMessage);
            await rent.DidNotReceiveWithAnyArgs().CreateExtraChargesAsync(default!, default, default, default!, default, default, default, default, default);
        }
    }
}
