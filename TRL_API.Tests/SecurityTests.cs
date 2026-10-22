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
        // Only these may be called without a login; everything else needs a login AND a permission check
        private static readonly string[] Anonymous = { "AuthController.Login", "AuthController.Refresh", "AuthController.Logout" };

        private static IEnumerable<(Type Controller, MethodInfo Action)> Actions() =>
            typeof(TRL_API.Controllers.AuthController).Assembly.GetTypes()
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
                .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .Where(m => m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Any())
                    .Select(m => (t, m)));

        private static IEnumerable<T> Both<T>(Type controller, MethodInfo action) where T : Attribute =>
            controller.GetCustomAttributes<T>().Concat(action.GetCustomAttributes<T>());

        // Signed in ([Authorize]) and checked against the user's current access by one of the permission attributes
        private static bool Protected(Type controller, MethodInfo action) =>
            Both<AuthorizeAttribute>(controller, action).Any()
            && (Both<RequirePermissionAttribute>(controller, action).Any() || Both<SignedInUserAttribute>(controller, action).Any()
                || Both<SuperAdminOnlyAttribute>(controller, action).Any());

        [Fact]
        public void Every_endpoint_needs_a_login_and_a_permission_check_except_login_refresh_logout()
        {
            var actions = Actions().ToList();
            Assert.True(actions.Count > 80, $"only {actions.Count} actions found");
            var open = actions.Where(x => !Protected(x.Controller, x.Action)).Select(x => $"{x.Controller.Name}.{x.Action.Name}").OrderBy(n => n).ToList();
            Assert.Equal(Anonymous.OrderBy(n => n), open);
        }

        [Fact]
        public void Every_required_permission_is_a_known_one()
        {
            var unknown = Actions().SelectMany(x => Both<RequirePermissionAttribute>(x.Controller, x.Action)
                    .SelectMany(a => a.AnyOf.Select(p => (Name: $"{x.Controller.Name}.{x.Action.Name}", p))))
                .Where(x => !Permissions.All.Contains(x.p)).Select(x => $"{x.Name}: {x.p}");
            Assert.Empty(unknown);
            Assert.All(Actions().SelectMany(x => Both<RequirePermissionAttribute>(x.Controller, x.Action)), a => Assert.NotEmpty(a.AnyOf));
        }

        [Fact]
        public void Changing_data_needs_an_edit_permission_or_super_admin()
        {
            // Every POST/PUT/PATCH/DELETE (except auth and the user's own password) needs some "<page>:edit" or the Super Admin
            var weak = Actions()
                .Where(x => x.Controller != typeof(TRL_API.Controllers.AuthController))
                .Where(x => x.Action.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Any(h => !h.HttpMethods.Contains("GET")))
                .Where(x => !Both<SuperAdminOnlyAttribute>(x.Controller, x.Action).Any()
                            && !Both<RequirePermissionAttribute>(x.Controller, x.Action).Any(a => a.AnyOf.All(p => p.EndsWith(":edit"))))
                .Select(x => $"{x.Controller.Name}.{x.Action.Name}");
            Assert.Empty(weak);
        }

        [Fact]
        public void Risky_actions_need_their_own_permission()
        {
            static string[] Needs(Type c, string action) =>
                c.GetMethod(action)!.GetCustomAttributes<RequirePermissionAttribute>().SelectMany(a => a.AnyOf).ToArray();
            Assert.Contains(Permissions.ReversePayment, Needs(typeof(TRL_API.Controllers.RentController), "ReversePayment"));
            Assert.Contains(Permissions.LateFee, Needs(typeof(TRL_API.Controllers.RentController), "ChargeLateFee"));
            Assert.Contains(Permissions.LateFee, Needs(typeof(TRL_API.Controllers.RentController), "ReverseLateFee"));
            Assert.Contains(Permissions.EndLease, Needs(typeof(TRL_API.Controllers.LeaseController), "Terminate"));
            foreach (var (c, a) in new[] {
                (typeof(TRL_API.Controllers.LeaseController), "Cancel"), (typeof(TRL_API.Controllers.LeaseController), "CancelRenewal"),
                (typeof(TRL_API.Controllers.RentHistoryController), "CancelInvoice"), (typeof(TRL_API.Controllers.RentHistoryController), "ReinstateInvoice"),
                (typeof(TRL_API.Controllers.TenantController), "DeleteTenant"), (typeof(TRL_API.Controllers.PropertiesController), "DeleteBuilding"),
                (typeof(TRL_API.Controllers.PropertiesController), "DeleteFloor"), (typeof(TRL_API.Controllers.PropertiesController), "DeleteUnit") })
                Assert.Contains(Permissions.DeleteCancel, Needs(c, a));
            // Every action permission is used somewhere (discount is checked inside SubmitPayments)
            var used = Actions().SelectMany(x => Both<RequirePermissionAttribute>(x.Controller, x.Action).SelectMany(a => a.AnyOf)).ToHashSet();
            Assert.All(Permissions.Actions.Where(p => p != Permissions.Discount), p => Assert.Contains(p, used));
        }

        [Fact]
        public void Users_page_is_super_admin_only()
        {
            Assert.NotNull(typeof(TRL_API.Controllers.UsersController).GetCustomAttribute<SuperAdminOnlyAttribute>());
        }

        [Fact]
        public void Permissions_are_normalized_and_edit_includes_view()
        {
            var stored = Permissions.Normalize(new[] { "tenants:view", "tenants:edit", "collectionsReport:view", "collectionsReport:edit", "payments:view", "bogus", "action:discount", "dashboard:view", null! });
            // unknown, removed (payments = old Payment Records page) and impossible ones dropped (reports have no edit level),
            // edit kept instead of view
            Assert.Equal(new[] { "action:discount", "collectionsReport:view", "dashboard:view", "tenants:edit" }, stored);
            var granted = stored.ToHashSet();
            Assert.True(Permissions.Grants(granted, "tenants:view"));
            Assert.True(Permissions.Grants(granted, "tenants:edit"));
            Assert.False(Permissions.Grants(granted, "collectionsReport:edit"));
            Assert.DoesNotContain("payments:view", Permissions.All); // Payment Records was merged into Collections
            Assert.False(Permissions.Grants(granted, "leases:view"));
            Assert.False(Permissions.Grants(granted, "action:reversePayment"));
            Assert.True(new AccessInfo(1, "x", null, true, new HashSet<string>()).Can("action:reversePayment"));
            Assert.False(new AccessInfo(1, "x", null, false, new HashSet<string>()).Can("dashboard:view"));
            Assert.True(Permissions.CanLogIn("Admin") && Permissions.CanLogIn("User"));
            Assert.False(Permissions.CanLogIn("Tenant") || Permissions.CanLogIn(null) || Permissions.CanLogIn("admin"));
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
