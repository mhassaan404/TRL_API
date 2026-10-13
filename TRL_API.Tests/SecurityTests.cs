using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TRL_API.Helpers;
using Xunit;

namespace TRL_API.Tests
{
    public class SecurityTests
    {
        // Only these may be called without an Admin login; everything else must require Role = Admin
        private static readonly string[] Anonymous = { "AuthController.Login", "AuthController.Refresh", "AuthController.Logout" };

        private static IEnumerable<(Type Controller, MethodInfo Action)> Actions() =>
            typeof(TRL_API.Controllers.AuthController).Assembly.GetTypes()
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
                .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .Where(m => m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Any())
                    .Select(m => (t, m)));

        private static bool RequiresAdmin(Type controller, MethodInfo action) =>
            controller.GetCustomAttributes<AuthorizeAttribute>().Concat(action.GetCustomAttributes<AuthorizeAttribute>())
                .Any(a => (a.Roles ?? "").Split(',').Select(r => r.Trim()).SequenceEqual(new[] { "Admin" }));

        [Fact]
        public void Every_endpoint_requires_Admin_except_login_refresh_logout()
        {
            var actions = Actions().ToList();
            Assert.True(actions.Count > 80, $"only {actions.Count} actions found");
            var open = actions.Where(x => !RequiresAdmin(x.Controller, x.Action)).Select(x => $"{x.Controller.Name}.{x.Action.Name}").OrderBy(n => n).ToList();
            Assert.Equal(Anonymous.OrderBy(n => n), open);
        }

        [Fact]
        public void No_endpoint_is_marked_AllowAnonymous()
        {
            var marked = Actions().Where(x => x.Controller.GetCustomAttribute<AllowAnonymousAttribute>() != null
                                              || x.Action.GetCustomAttribute<AllowAnonymousAttribute>() != null)
                .Select(x => $"{x.Controller.Name}.{x.Action.Name}");
            Assert.Empty(marked);
        }

        private static string TempDir() => Path.Combine(Path.GetTempPath(), "trl-log-test-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public void File_log_keeps_warnings_and_errors_only()
        {
            var dir = TempDir();
            using var provider = new FileLoggerProvider(dir);
            var log = provider.CreateLogger("Test");
            log.LogInformation("info line");
            log.LogWarning("warning line {Value}", 42);
            log.LogError(new InvalidOperationException("boom"), "error line");
            var text = File.ReadAllText(Directory.GetFiles(dir, "trl-*.log").Single());
            Assert.DoesNotContain("info line", text);
            Assert.Contains("[WARN] Test: warning line 42", text);
            Assert.Contains("[ERROR] Test: error line", text);
            Assert.Contains("System.InvalidOperationException: boom", text);
            Directory.Delete(dir, true);
        }

        [Fact]
        public void File_log_flattens_line_breaks_so_a_username_cannot_fake_a_log_line()
        {
            var dir = TempDir();
            using var provider = new FileLoggerProvider(dir);
            provider.CreateLogger("Auth").LogWarning("Login failed for {User}", "eve\r\n2026-01-01 00:00:00Z [WARN] Auth: Password changed for admin");
            var lines = File.ReadAllLines(Directory.GetFiles(dir, "trl-*.log").Single());
            Assert.Single(lines);
            Directory.Delete(dir, true);
        }

        [Fact]
        public void File_log_deletes_files_older_than_the_kept_days_only()
        {
            var dir = TempDir();
            Directory.CreateDirectory(dir);
            var old = Path.Combine(dir, $"trl-{DateTime.UtcNow.AddDays(-40):yyyy-MM-dd}.log");
            var recent = Path.Combine(dir, $"trl-{DateTime.UtcNow.AddDays(-5):yyyy-MM-dd}.log");
            var other = Path.Combine(dir, "stdout_123.log");
            foreach (var f in new[] { old, recent, other }) File.WriteAllText(f, "x");
            using var provider = new FileLoggerProvider(dir, keepDays: 30);
            provider.CreateLogger("Test").LogWarning("today");
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(recent));
            Assert.True(File.Exists(other));
            Directory.Delete(dir, true);
        }

        [Fact]
        public void File_log_never_throws_when_the_folder_cannot_be_written()
        {
            var file = Path.GetTempFileName();   // a file where the folder should be: every write fails
            using var provider = new FileLoggerProvider(file);
            var ex = Record.Exception(() => provider.CreateLogger("Test").LogError("still fine"));
            Assert.Null(ex);
            File.Delete(file);
        }
    }
}
