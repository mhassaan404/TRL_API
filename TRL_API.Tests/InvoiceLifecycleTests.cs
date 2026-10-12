using System.Data;
using NSubstitute;
using TRL_API.BLL;
using TRL_API.DAL;
using TRL_API.Models;
using Xunit;

namespace TRL_API.Tests
{
    // Rent generation outcomes (window, no lease, already invoiced, mixed selection) and the cancel / reinstate /
    // late-fee messages. The SQL behind them is covered end to end in e2e/tests/invoices.mjs.
    public class InvoiceLifecycleTests
    {
        private static readonly DateTime ThisMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);

        private static DataTable Charges(params (int tenantId, bool alreadyInvoiced)[] rows)
        {
            var dt = new DataTable();
            foreach (var c in new[] { "TenantId", "LeaseId", "UnitId" }) dt.Columns.Add(c, typeof(int));
            dt.Columns.Add("Amount", typeof(decimal));
            dt.Columns.Add("FromDate", typeof(DateTime));
            dt.Columns.Add("Descr", typeof(string));
            dt.Columns.Add("AlreadyInvoiced", typeof(bool));
            foreach (var r in rows) dt.Rows.Add(r.tenantId, 100 + r.tenantId, 200 + r.tenantId, 40000m, ThisMonth, DBNull.Value, r.alreadyInvoiced);
            return dt;
        }

        private static DataTable Names(params (int id, string name)[] rows)
        {
            var dt = new DataTable();
            dt.Columns.Add("TenantId", typeof(int));
            dt.Columns.Add("Name", typeof(string));
            foreach (var r in rows) dt.Rows.Add(r.id, r.name);
            return dt;
        }

        private static (RentService svc, IRentRepository rent) Rent(DataTable charges, DataTable? names = null)
        {
            var rent = Substitute.For<IRentRepository>();
            rent.GetLeaseChargesForMonth(default, default).ReturnsForAnyArgs(charges);
            rent.GetTenantNamesAsync().Returns(names ?? Names());
            rent.CreateInvoice(default, default, default, default, default, default).ReturnsForAnyArgs(new ApiResponse { IsSuccess = true, RowsAffected = 1 });
            var settings = Substitute.For<ILateFeeSettingsRepository>();
            settings.GetAsync().Returns(new LateFeeSettings { PaymentDueDays = 5, LateFeePerDay = 500, MaxLateFeeMultiplier = 2 });
            return (new RentService(rent, settings), rent);
        }

        [Theory]
        [InlineData(-2)]
        [InlineData(2)]
        public async Task Only_last_this_or_next_month_can_be_generated(int monthsAway)
        {
            var (svc, rent) = Rent(Charges((1, false)));
            var m = ThisMonth.AddMonths(monthsAway);
            var r = await svc.GenerateInvoicesAsync(m.Month, m.Year, null, new List<int> { 1 });
            Assert.False(r.IsSuccess);
            Assert.StartsWith("Rent can only be generated for", r.ErrorMessage);
            await rent.DidNotReceiveWithAnyArgs().CreateInvoice(default, default, default, default, default, default);
        }

        [Fact]
        public async Task Invalid_month_is_refused()
        {
            var (svc, _) = Rent(Charges());
            Assert.Equal("Select a valid month and year.", (await svc.GenerateInvoicesAsync(13, ThisMonth.Year, null, null)).ErrorMessage);
        }

        [Fact]
        public async Task Tenant_without_a_lease_is_named_in_the_error()
        {
            var (svc, rent) = Rent(Charges(), Names((5, "Bilal")));
            var r = await svc.GenerateInvoicesAsync(ThisMonth.Month, ThisMonth.Year, null, new List<int> { 5 });
            Assert.False(r.IsSuccess);
            Assert.Contains("No lease covers", r.ErrorMessage);
            Assert.Contains("Bilal", r.ErrorMessage);
            await rent.DidNotReceiveWithAnyArgs().CreateInvoice(default, default, default, default, default, default);
        }

        [Fact]
        public async Task Running_again_creates_nothing_and_says_so()
        {
            var (svc, rent) = Rent(Charges((1, true)));
            var r = await svc.GenerateInvoicesAsync(ThisMonth.Month, ThisMonth.Year, null, new List<int> { 1 });
            Assert.True(r.IsSuccess);
            Assert.Equal(0, r.RowsAffected);
            Assert.Contains("already invoiced", r.Message);
            await rent.DidNotReceiveWithAnyArgs().CreateInvoice(default, default, default, default, default, default);
        }

        [Fact]
        public async Task Mixed_selection_bills_the_new_ones_and_names_the_skipped()
        {
            var (svc, rent) = Rent(Charges((1, true), (2, false)), Names((3, "Sara")));
            var r = await svc.GenerateInvoicesAsync(ThisMonth.Month, ThisMonth.Year, null, new List<int> { 1, 2, 3 });
            Assert.True(r.IsSuccess);
            Assert.Equal(1, r.RowsAffected);
            Assert.Contains("Skipped (no lease for this month): Sara", r.Message);
            await rent.Received(1).CreateInvoice(2, 40000m, ThisMonth, ThisMonth.AddDays(5), 500m, 2m, null, null, 102, 202);
        }

        [Fact]
        public async Task All_tenants_ignores_tenants_selection_and_bills_every_uninvoiced_lease()
        {
            var (svc, rent) = Rent(Charges((1, false), (2, false), (3, true)));
            var r = await svc.GenerateInvoicesAsync(ThisMonth.Month, ThisMonth.Year, null, null);
            Assert.Equal(2, r.RowsAffected);
            await rent.ReceivedWithAnyArgs(2).CreateInvoice(default, default, default, default, default, default);
        }

        private static DataTable Result(string value)
        {
            var dt = new DataTable();
            dt.Columns.Add("Result", typeof(string));
            dt.Rows.Add(value);
            return dt;
        }

        [Theory]
        [InlineData("OK", true, "Invoice cancelled successfully.")]
        [InlineData("HAS_PAYMENTS", false, "Reverse them first")]
        [InlineData("SETTLED", false, "part of a move-out settlement")]
        [InlineData("NOT_FOUND", false, "not found or already cancelled")]
        public async Task Cancel_invoice_results(string code, bool success, string text)
        {
            var dal = Substitute.For<IRentHistoryRepository>();
            dal.CancelInvoice(default, default!, default).ReturnsForAnyArgs(Result(code));
            var r = await new RentHistoryService(dal).CancelInvoice(4, " wrong month ", 1);
            Assert.Equal(success, r.IsSuccess);
            Assert.Contains(text, success ? r.Message : r.ErrorMessage);
            await dal.Received(1).CancelInvoice(4, "wrong month", 1);
        }

        [Fact]
        public async Task Cancel_needs_a_reason_that_fits_the_audit_log()
        {
            var dal = Substitute.For<IRentHistoryRepository>();
            var svc = new RentHistoryService(dal);
            Assert.Equal("Reason is required to cancel an invoice.", (await svc.CancelInvoice(4, "  ", 1)).ErrorMessage);
            Assert.Equal("The reason can be at most 300 characters.", (await svc.CancelInvoice(4, new string('x', 301), 1)).ErrorMessage);
            await dal.DidNotReceiveWithAnyArgs().CancelInvoice(default, default!, default);
        }

        [Fact]
        public async Task Late_fee_reverse_reason_fits_the_audit_log()
        {
            var rent = Substitute.For<IRentRepository>();
            var svc = new RentService(rent, Substitute.For<ILateFeeSettingsRepository>());
            Assert.Equal("The reason can be at most 300 characters.", (await svc.ReverseLateFeeAsync(1, new string('x', 301), 1)).ErrorMessage);
            await rent.DidNotReceiveWithAnyArgs().ReverseLateFeeAsync(default, default!, default);
        }

        [Theory]
        [InlineData("OK", true, "Invoice reinstated successfully.")]
        [InlineData("SETTLED", false, "move-out settlement")]
        [InlineData("NOT_FOUND", false, "not cancelled")]
        [InlineData("NOT_ALLOWED", false, "lease no longer covers that month")]
        public async Task Reinstate_results(string code, bool success, string text)
        {
            var dal = Substitute.For<IRentHistoryRepository>();
            dal.ReinstateInvoice(default, default).ReturnsForAnyArgs(code);
            var r = await new RentHistoryService(dal).ReinstateInvoice(4, 9);
            Assert.Equal(success, r.IsSuccess);
            Assert.Contains(text, success ? r.Message : r.ErrorMessage);
            await dal.Received(1).ReinstateInvoice(4, 9); // the user is passed on for the audit row
        }

        [Theory]
        [InlineData(0, 0, "Select at least one invoice.")]
        [InlineData(501, 0, "Update at most 500 invoices at a time.")]
        [InlineData(1, -1000, "Please choose a due date no more than a year from today.")]
        [InlineData(1, 367, "Please choose a due date no more than a year from today.")]
        public void Due_date_change_request_is_checked(int count, int daysFromToday, string message)
        {
            var ids = Enumerable.Range(1, count).ToList();
            var date = daysFromToday == -1000 ? DateTime.MinValue : DateTime.Today.AddDays(daysFromToday);
            Assert.Equal(message, RentService.ValidateDueDateChange(ids, date, DateTime.Today));
        }

        [Fact]
        public void Due_date_change_rejects_invalid_ids_and_accepts_a_normal_request()
        {
            Assert.Equal("One or more selected invoices are invalid.", RentService.ValidateDueDateChange(new List<int> { 3, 0 }, DateTime.Today, DateTime.Today));
            Assert.Null(RentService.ValidateDueDateChange(new List<int> { 3 }, DateTime.Today.AddDays(10), DateTime.Today));
        }

        private static DataTable DueDateResult(int updated, int requested)
        {
            var dt = new DataTable();
            dt.Columns.Add("Updated", typeof(int));
            dt.Columns.Add("Requested", typeof(int));
            dt.Rows.Add(updated, requested);
            return dt;
        }

        [Theory]
        [InlineData(3, 3, true, "Due date updated for 3 invoice(s).")]
        [InlineData(2, 3, true, "1 skipped: only unpaid or partly paid invoices")]
        [InlineData(0, 2, false, "No due date was changed")]
        public async Task Due_date_change_reports_updated_and_skipped(int updated, int requested, bool success, string text)
        {
            var rent = Substitute.For<IRentRepository>();
            rent.BulkUpdateDueDateAsync(default!, default, default).ReturnsForAnyArgs(DueDateResult(updated, requested));
            var svc = new RentService(rent, Substitute.For<ILateFeeSettingsRepository>());
            var r = await svc.BulkUpdateDueDateAsync(new List<int> { 1, 2, 3 }, DateTime.Today.AddDays(5), 4);
            Assert.Equal(success, r.IsSuccess);
            Assert.Contains(text, success ? r.Message : r.ErrorMessage);
            await rent.Received(1).BulkUpdateDueDateAsync(Arg.Any<List<int>>(), DateTime.Today.AddDays(5), 4);
        }

        [Fact]
        public async Task Late_fee_reverse_needs_a_reason_and_reports_nothing_to_reverse()
        {
            var rent = Substitute.For<IRentRepository>();
            var fee = new DataTable();
            fee.Columns.Add("Fee", typeof(decimal));
            fee.Rows.Add(0m);
            rent.ReverseLateFeeAsync(default, default!, default).ReturnsForAnyArgs(fee);
            var svc = new RentService(rent, Substitute.For<ILateFeeSettingsRepository>());
            Assert.Equal("Reason is required.", (await svc.ReverseLateFeeAsync(1, " ", 1)).ErrorMessage);
            await rent.DidNotReceiveWithAnyArgs().ReverseLateFeeAsync(default, default!, default);
            Assert.StartsWith("Nothing to reverse", (await svc.ReverseLateFeeAsync(1, "goodwill", 1)).ErrorMessage);
            await rent.Received(1).ReverseLateFeeAsync(1, "goodwill", 1);
        }
    }
}
