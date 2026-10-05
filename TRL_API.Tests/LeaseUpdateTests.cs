using NSubstitute;
using TRL_API.BLL;
using TRL_API.DAL;
using TRL_API.Models;
using Xunit;

namespace TRL_API.Tests
{
    // Edit lease: input checks in LeaseService (the billing rules run in SQL and are covered by the API tests)
    public class LeaseUpdateTests
    {
        private static (LeaseService svc, ILeaseRepository repo) Create()
        {
            var repo = Substitute.For<ILeaseRepository>();
            repo.UpdateAsync(default, default, default, default).ReturnsForAnyArgs(new ApiResponse { IsSuccess = true });
            return (new LeaseService(repo), repo);
        }

        [Fact]
        public async Task Valid_edit_is_passed_to_the_repository_with_the_date_only()
        {
            var (svc, repo) = Create();
            var res = await svc.UpdateAsync(new UpdateLeaseRequest { LeaseId = 7, StartDate = new DateTime(2026, 9, 10, 15, 30, 0), RentAmount = 32000, TenureMonths = 12 });

            Assert.True(res.IsSuccess);
            await repo.Received(1).UpdateAsync(7, new DateTime(2026, 9, 10), 32000m, 12);
        }

        public static IEnumerable<object[]> Invalid() => new[]
        {
            new object[] { new UpdateLeaseRequest { LeaseId = 0, StartDate = DateTime.Today, RentAmount = 1, TenureMonths = 1 }, "Lease is required." },
            new object[] { new UpdateLeaseRequest { LeaseId = 1, StartDate = null, RentAmount = 1, TenureMonths = 1 }, "Start date is required." },
            new object[] { new UpdateLeaseRequest { LeaseId = 1, StartDate = DateTime.Today, RentAmount = 0, TenureMonths = 1 }, "Rent must be greater than zero." },
            new object[] { new UpdateLeaseRequest { LeaseId = 1, StartDate = DateTime.Today, RentAmount = null, TenureMonths = 1 }, "Rent must be greater than zero." },
            new object[] { new UpdateLeaseRequest { LeaseId = 1, StartDate = DateTime.Today, RentAmount = 1, TenureMonths = 0 }, "Tenure must be at least 1 month." },
            new object[] { new UpdateLeaseRequest { LeaseId = 1, StartDate = DateTime.Today, RentAmount = 1, TenureMonths = null }, "Tenure must be at least 1 month." },
        };

        [Theory]
        [MemberData(nameof(Invalid))]
        public async Task Invalid_edit_is_rejected_before_the_database(UpdateLeaseRequest req, string message)
        {
            var (svc, repo) = Create();
            var res = await svc.UpdateAsync(req);

            Assert.False(res.IsSuccess);
            Assert.Equal(message, res.ErrorMessage);
            await repo.DidNotReceiveWithAnyArgs().UpdateAsync(default, default, default, default);
        }
    }
}
