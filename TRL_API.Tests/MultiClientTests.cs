using Microsoft.Extensions.Caching.Memory;
using TRL_API.Data;
using TRL_API.Services;
using Xunit;

namespace TRL_API.Tests
{
    // Multi-client guards that don't need a database (isolation itself is tested end to end over HTTP on temp databases)
    public class MultiClientTests
    {
        [Fact]
        public void The_API_requires_the_newest_migration_file()
        {
            // Walk up from the test output folder to the repository's Database/migrations
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Database", "migrations"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var newest = Directory.GetFiles(Path.Combine(dir!.FullName, "Database", "migrations"), "*.sql")
                .Select(Path.GetFileNameWithoutExtension).OrderBy(n => n, StringComparer.Ordinal).Last();
            Assert.Equal(newest, SchemaVersion.Required);
        }

        [Fact]
        public void Login_lockout_is_per_client_code()
        {
            var throttle = new LoginThrottle(new MemoryCache(new MemoryCacheOptions()));
            for (int i = 0; i < LoginThrottle.MaxFailures; i++) throttle.RecordFailure("TRL", "admin", "1.2.3.4");

            Assert.NotNull(throttle.LockedMinutesLeft("TRL", "admin", "1.2.3.4"));
            Assert.NotNull(throttle.LockedMinutesLeft(" trl ", "ADMIN", "1.2.3.4"));   // same client/user, any case
            Assert.Null(throttle.LockedMinutesLeft("CLB", "admin", "1.2.3.4"));        // same username at another client
            Assert.Null(throttle.LockedMinutesLeft("TRL", "admin", "5.6.7.8"));        // another address

            throttle.Reset("TRL", "admin", "1.2.3.4");
            Assert.Null(throttle.LockedMinutesLeft("TRL", "admin", "1.2.3.4"));
        }

        [Fact]
        public void Without_a_client_there_is_no_database()
        {
            var http = new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };
            var context = new HttpClientContext(http);
            Assert.Throws<UnauthorizedAccessException>(() => context.ConnectionString);
        }
    }
}
