using NSubstitute;
using TRL_API.BLL;
using TRL_API.DAL;
using TRL_API.Models;
using Xunit;

namespace TRL_API.Tests
{
    // Server-side validation of the Late Fee Settings page (same limits as the page and the DB CHECK constraints)
    public class LateFeeSettingsServiceTests
    {
        private static SaveLateFeeSettingsRequest Req(int? due = 5, decimal? perDay = 500, decimal? max = 2) =>
            new() { PaymentDueDays = due, LateFeePerDay = perDay, MaxLateFeeMultiplier = max };

        private static (LateFeeSettingsService svc, ILateFeeSettingsRepository repo) Create()
        {
            var repo = Substitute.For<ILateFeeSettingsRepository>();
            repo.SaveAsync(default, default, default, default).ReturnsForAnyArgs(new ApiResponse { IsSuccess = true, RowsAffected = 1 });
            return (new LateFeeSettingsService(repo), repo);
        }

        [Fact]
        public async Task Valid_settings_are_saved_with_the_user()
        {
            var (svc, repo) = Create();
            var res = await svc.SaveAsync(Req(7, 750, 1.5m), userId: 42);

            Assert.True(res.IsSuccess);
            await repo.Received(1).SaveAsync(7, 750m, 1.5m, 42);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(90)]
        public async Task Due_days_limits_are_inclusive(int days)
        {
            var (svc, _) = Create();
            Assert.True((await svc.SaveAsync(Req(due: days), 1)).IsSuccess);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(100000)]
        public async Task Fee_per_day_limits_are_inclusive(int fee)
        {
            var (svc, _) = Create();
            Assert.True((await svc.SaveAsync(Req(perDay: fee), 1)).IsSuccess);
        }

        [Theory]
        [InlineData("0.1")]
        [InlineData("1.5")]
        [InlineData("12")]
        public async Task Multiplier_limits_are_inclusive(string max)
        {
            var (svc, _) = Create();
            Assert.True((await svc.SaveAsync(Req(max: decimal.Parse(max, System.Globalization.CultureInfo.InvariantCulture)), 1)).IsSuccess);
        }

        public static IEnumerable<object?[]> InvalidRequests() => new[]
        {
            new object?[] { null, "Please enter the late fee settings." },
            new object?[] { Req(due: null), "Please enter the payment due days." },
            new object?[] { Req(due: -1), "Payment due days must be a whole number from 0 to 90." },
            new object?[] { Req(due: 91), "Payment due days must be a whole number from 0 to 90." },
            new object?[] { Req(perDay: null), "Please enter the late fee per day." },
            new object?[] { Req(perDay: 0), "Late fee per day must be a whole rupee amount from 1 to 100,000." },
            new object?[] { Req(perDay: -500), "Late fee per day must be a whole rupee amount from 1 to 100,000." },
            new object?[] { Req(perDay: 500.5m), "Late fee per day must be a whole rupee amount from 1 to 100,000." },
            new object?[] { Req(perDay: 100001), "Late fee per day must be a whole rupee amount from 1 to 100,000." },
            new object?[] { Req(max: null), "Please enter the maximum late fee." },
            new object?[] { Req(max: 0), "Maximum late fee must be greater than 0 and at most 12 times the invoice rent." },
            new object?[] { Req(max: 12.1m), "Maximum late fee must be greater than 0 and at most 12 times the invoice rent." },
            new object?[] { Req(max: 1.25m), "Maximum late fee can have at most one decimal place (e.g. 1.5)." },
        };

        [Theory]
        [MemberData(nameof(InvalidRequests))]
        public async Task Invalid_settings_are_rejected_and_not_saved(SaveLateFeeSettingsRequest? req, string message)
        {
            var (svc, repo) = Create();
            var res = await svc.SaveAsync(req!, 1);

            Assert.False(res.IsSuccess);
            Assert.Equal(message, res.ErrorMessage);
            await repo.DidNotReceiveWithAnyArgs().SaveAsync(default, default, default, default);
        }
    }
}
