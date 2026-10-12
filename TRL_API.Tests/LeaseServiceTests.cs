using System.Data;
using NSubstitute;
using TRL_API.BLL;
using TRL_API.DAL;
using TRL_API.Models;
using Xunit;

namespace TRL_API.Tests
{
    // Lease create / renew / end / cancel: request checks that run before the database, and what is passed on.
    // The database rules themselves (overlaps, renewals, re-pricing) are covered end to end in e2e/tests/leases.mjs.
    public class LeaseServiceTests
    {
        private static (LeaseService svc, ILeaseRepository dal) Create()
        {
            var dal = Substitute.For<ILeaseRepository>();
            dal.CreateAsync(default!, default).ReturnsForAnyArgs(Result("OK"));
            dal.RenewAsync(default!, default).ReturnsForAnyArgs(new ApiResponse { IsSuccess = true });
            dal.TerminateAsync(default!, default, default).ReturnsForAnyArgs(new ApiResponse { IsSuccess = true });
            dal.CancelRenewalAsync(default, default).ReturnsForAnyArgs(new ApiResponse { IsSuccess = true });
            dal.CancelLeaseAsync(default, default).ReturnsForAnyArgs(new ApiResponse { IsSuccess = true });
            return (new LeaseService(dal), dal);
        }

        private static DataTable Result(string code, DateTime? info = null)
        {
            var dt = new DataTable();
            dt.Columns.Add("Result", typeof(string));
            dt.Columns.Add("Info", typeof(DateTime));
            dt.Rows.Add(code, (object?)info ?? DBNull.Value);
            return dt;
        }

        private static Lease Lease(int tenant = 1, int unit = 2, decimal rent = 40000, int tenure = 12) =>
            new() { TenantId = tenant, UnitId = unit, RentAmount = rent, StartDate = DateTime.Today, TenureMonths = tenure };

        [Theory]
        [InlineData(0, 2, 40000, 12, "Tenant and unit are required.")]
        [InlineData(1, 0, 40000, 12, "Tenant and unit are required.")]
        [InlineData(1, 2, 0, 12, "Rent must be greater than zero.")]
        [InlineData(1, 2, -5, 12, "Rent must be greater than zero.")]
        [InlineData(1, 2, 40000, 0, "Tenure must be at least 1 month.")]
        public async Task Invalid_new_lease_is_rejected_before_the_database(int tenant, int unit, decimal rent, int tenure, string message)
        {
            var (svc, dal) = Create();
            var r = await svc.CreateAsync(Lease(tenant, unit, rent, tenure), 1);
            Assert.False(r.IsSuccess);
            Assert.Equal(message, r.ErrorMessage);
            await dal.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
        }

        [Theory]
        [InlineData("UNIT_NOT_FOUND", "The unit doesn't exist or has been deleted.")]
        [InlineData("TENANT_NOT_FOUND", "The tenant doesn't exist or has been deleted.")]
        [InlineData("UNIT_LEASED", "This unit already has an active lease.")]
        public async Task Database_checks_are_reported(string code, string message)
        {
            var (svc, dal) = Create();
            dal.CreateAsync(default!, default).ReturnsForAnyArgs(Result(code));
            var r = await svc.CreateAsync(Lease(), 1);
            Assert.False(r.IsSuccess);
            Assert.Equal(message, r.ErrorMessage);
        }

        [Fact]
        public async Task New_lease_cannot_start_inside_an_earlier_billed_period()
        {
            var (svc, dal) = Create();
            dal.CreateAsync(default!, default).ReturnsForAnyArgs(Result("OVERLAP", new DateTime(2026, 10, 31)));
            var r = await svc.CreateAsync(Lease(), 1);
            Assert.False(r.IsSuccess);
            Assert.Contains("billed to an earlier lease through 31 Oct 2026", r.ErrorMessage);
        }

        [Fact]
        public async Task Impossible_start_dates_are_rejected_before_the_database()
        {
            var (svc, dal) = Create();
            foreach (var d in new[] { DateTime.MinValue, DateTime.Today.AddYears(3) })
            {
                var lease = Lease();
                lease.StartDate = d;
                Assert.Equal("Please choose a start date no more than two years from today.", (await svc.CreateAsync(lease, 1)).ErrorMessage);
            }
            await dal.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
        }

        [Fact]
        public async Task Valid_new_lease_is_created()
        {
            var (svc, dal) = Create();
            var lease = Lease();
            var r = await svc.CreateAsync(lease, 7);
            Assert.True(r.IsSuccess);
            await dal.Received(1).CreateAsync(lease, 7);
        }

        [Theory]
        [InlineData(0, null, "Tenure must be at least 1 month.")]
        [InlineData(12, 0, "New rent must be greater than zero (leave it empty to keep the current rent).")]
        [InlineData(12, -100, "New rent must be greater than zero (leave it empty to keep the current rent).")]
        public async Task Invalid_renewal_is_rejected(int tenure, int? rent, string message)
        {
            var (svc, dal) = Create();
            var r = await svc.RenewAsync(new RenewLeaseRequest { LeaseId = 1, TenureMonths = tenure, NewRentAmount = rent }, 1);
            Assert.Equal(message, r.ErrorMessage);
            await dal.DidNotReceiveWithAnyArgs().RenewAsync(default!, default);
        }

        [Fact]
        public async Task Renewal_without_a_new_rent_keeps_the_current_rent()
        {
            var (svc, dal) = Create();
            var r = await svc.RenewAsync(new RenewLeaseRequest { LeaseId = 1, TenureMonths = 12, NewRentAmount = null }, 1);
            Assert.True(r.IsSuccess);
            await dal.Received(1).RenewAsync(Arg.Is<RenewLeaseRequest>(x => x.NewRentAmount == null), 1);
        }

        [Theory]
        [InlineData(null, "Please enter a reason for ending the lease.")]
        [InlineData("   ", "Please enter a reason for ending the lease.")]
        public async Task Ending_a_lease_needs_a_reason(string? reason, string message)
        {
            var (svc, dal) = Create();
            var r = await svc.TerminateAsync(new TerminateLeaseRequest { LeaseId = 1, Reason = reason! }, 1);
            Assert.Equal(message, r.ErrorMessage);
            await dal.DidNotReceiveWithAnyArgs().TerminateAsync(default!, default, default);
        }

        [Fact]
        public async Task Ending_a_lease_rejects_a_long_reason_and_a_future_move_out()
        {
            var (svc, dal) = Create();
            var r = await svc.TerminateAsync(new TerminateLeaseRequest { LeaseId = 1, Reason = new string('x', 201) }, 1);
            Assert.Equal("The reason can be at most 200 characters.", r.ErrorMessage);
            r = await svc.TerminateAsync(new TerminateLeaseRequest { LeaseId = 1, Reason = "moved", MoveOutDate = DateTime.Today.AddDays(1) }, 1);
            Assert.StartsWith("Move-out date can't be in the future", r.ErrorMessage);
            await dal.DidNotReceiveWithAnyArgs().TerminateAsync(default!, default, default);
        }

        [Fact]
        public async Task Ending_a_lease_trims_the_reason_and_defaults_the_move_out_to_today()
        {
            var (svc, dal) = Create();
            var r = await svc.TerminateAsync(new TerminateLeaseRequest { LeaseId = 1, Reason = "  moved abroad  " }, 3);
            Assert.True(r.IsSuccess);
            await dal.Received(1).TerminateAsync(Arg.Is<TerminateLeaseRequest>(x => x.Reason == "moved abroad"), DateTime.Today, 3);
        }

        [Fact]
        public async Task Cancel_and_cancel_renewal_need_a_lease()
        {
            var (svc, dal) = Create();
            Assert.Equal("Lease is required.", (await svc.CancelLeaseAsync(new CancelLeaseRequest { LeaseId = 0 }, 1)).ErrorMessage);
            Assert.Equal("Lease is required.", (await svc.CancelRenewalAsync(new CancelRenewalRequest { LeaseId = 0 }, 1)).ErrorMessage);
            await dal.DidNotReceiveWithAnyArgs().CancelLeaseAsync(default, default);
            await dal.DidNotReceiveWithAnyArgs().CancelRenewalAsync(default, default);
            Assert.True((await svc.CancelLeaseAsync(new CancelLeaseRequest { LeaseId = 5 }, 1)).IsSuccess);
            await dal.Received(1).CancelLeaseAsync(5, 1);
        }
    }
}
