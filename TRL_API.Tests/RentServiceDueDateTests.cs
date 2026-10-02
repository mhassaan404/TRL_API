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

        [Fact]
        public async Task Extra_charges_use_the_due_days_setting_and_keep_the_rate_and_cap()
        {
            var (svc, rent) = Create(Settings(10, 300, 1), LeaseCharges());

            var res = await svc.CreateExtraChargeAsync(new List<int> { 5, 6 }, ThisMonth.Month, ThisMonth.Year, "Maintenance", "Paint", 2500, null);

            Assert.True(res.IsSuccess);
            await rent.Received(1).CreateInvoice(5, 2500m, ThisMonth, ThisMonth.AddDays(10), 300m, 1m, "Paint", "Maintenance");
            await rent.Received(1).CreateInvoice(6, 2500m, ThisMonth, ThisMonth.AddDays(10), 300m, 1m, "Paint", "Maintenance");
        }
    }
}
