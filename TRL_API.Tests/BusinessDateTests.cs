using TRL_API.Helpers;
using Xunit;

namespace TRL_API.Tests
{
    // The business date is Pakistan's date (UTC+5), independent of the server's time zone
    public class BusinessDateTests
    {
        [Theory]
        [InlineData("2026-10-12T00:00:00", "2026-10-12")] // 05:00 in Pakistan
        [InlineData("2026-10-12T18:59:59", "2026-10-12")] // 23:59:59 in Pakistan
        [InlineData("2026-10-12T19:00:00", "2026-10-13")] // midnight in Pakistan: already tomorrow, while UTC is still the 12th
        [InlineData("2026-12-31T19:30:00", "2027-01-01")] // new year in Pakistan first
        public void Business_date_is_utc_plus_five(string utc, string expected) =>
            Assert.Equal(DateTime.Parse(expected), BusinessDate.TodayAt(DateTime.SpecifyKind(DateTime.Parse(utc), DateTimeKind.Utc)));

        [Fact]
        public void Today_has_no_time_part() => Assert.Equal(TimeSpan.Zero, BusinessDate.Today.TimeOfDay);

        [Fact]
        public void The_database_uses_the_same_offset()
        {
            // dbo.BusinessToday() (Database/02_functions.sql and the migration) must add the same number of hours
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Database"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var sql = File.ReadAllText(Path.Combine(dir!.FullName, "Database", "02_functions.sql"));
            Assert.Contains($"DATEADD(HOUR, {BusinessDate.UtcOffsetHours}, SYSUTCDATETIME())", sql);
        }
    }
}
