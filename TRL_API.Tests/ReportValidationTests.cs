using TRL_API.BLL;
using Xunit;

namespace TRL_API.Tests
{
    // Collections report date range checks (ReportService.ValidateCollectionsRange)
    public class ReportValidationTests
    {
        private static readonly ReportService Service = new(null!);
        private static string? Check(string? from, string? to) =>
            Service.ValidateCollectionsRange(from == null ? null : DateTime.Parse(from), to == null ? null : DateTime.Parse(to));

        [Theory]
        [InlineData("2026-10-01", "2026-10-08")]
        [InlineData("2026-10-08", "2026-10-08")]
        [InlineData("2026-10-08T15:00:00", "2026-10-08T09:00:00")] // same day, time ignored
        [InlineData("2021-10-10", "2026-10-14")] // 1830 days
        public void Valid_ranges(string from, string to) => Assert.Null(Check(from, to));

        [Theory]
        [InlineData(null, "2026-10-08", "Please choose a From and To date.")]
        [InlineData("2026-10-08", null, "Please choose a From and To date.")]
        [InlineData("2026-10-09", "2026-10-08", "From date must be on or before To date.")]
        [InlineData("2021-10-10", "2026-10-15", "Please choose a range of 5 years or less.")]
        [InlineData("1999-12-31", "2026-10-08", "Please choose dates between 2000 and 2100.")]
        [InlineData("2026-10-08", "9999-12-31", "Please choose dates between 2000 and 2100.")]
        public void Invalid_ranges(string? from, string? to, string message) => Assert.Equal(message, Check(from, to));
    }
}

namespace TRL_API.Tests
{
    // Billing vs Collection month range checks (ReportService.ValidateMonthRange)
    public class ReportMonthRangeTests
    {
        private static readonly ReportService Service = new(null!);
        private static string? Check(string? from, string? to) =>
            Service.ValidateMonthRange(from == null ? null : DateTime.Parse(from), to == null ? null : DateTime.Parse(to));

        [Theory]
        [InlineData("2026-01-01", "2026-12-01")]
        [InlineData("2026-10-31", "2026-10-01")] // same month; the day is ignored
        [InlineData("2022-01-01", "2026-12-31")] // exactly 60 months
        public void Valid_ranges(string from, string to) => Assert.Null(Check(from, to));

        [Theory]
        [InlineData(null, "2026-10-01", "Please choose a From and To month.")]
        [InlineData("2026-11-01", "2026-10-31", "From month must be on or before To month.")]
        [InlineData("2021-12-01", "2026-12-01", "Please choose 60 months or less.")]
        [InlineData("1999-12-01", "2000-01-01", "Please choose months between 2000 and 2100.")]
        public void Invalid_ranges(string? from, string? to, string message) => Assert.Equal(message, Check(from, to));
    }
}
