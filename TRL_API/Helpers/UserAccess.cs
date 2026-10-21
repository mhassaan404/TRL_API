using Microsoft.EntityFrameworkCore;
using TRL_API.Data;

namespace TRL_API.Helpers
{
    // The signed-in user's access, read fresh from their client's database once per request (so a change on the
    // Users page, or deactivating a user, applies to their very next action, not only after they log in again).
    public sealed record AccessInfo(int UserId, string Username, string? FullName, bool IsSuperAdmin, IReadOnlySet<string> Granted)
    {
        public bool Can(string permission) => IsSuperAdmin || Permissions.Grants(Granted, permission);

        // What the screen needs: everything for the Super Admin, otherwise the stored list
        public IEnumerable<string> List => IsSuperAdmin ? Permissions.All.OrderBy(p => p, StringComparer.Ordinal) : Granted.OrderBy(p => p, StringComparer.Ordinal);
    }

    public class UserAccess
    {
        private readonly IHttpContextAccessor _http;
        private readonly IClientContext _client;
        private Task<AccessInfo?>? _loaded;

        public UserAccess(IHttpContextAccessor http, IClientContext client)
        {
            _http = http;
            _client = client;
        }

        // null: no signed-in user, or the user no longer exists, is deactivated or has a role without access
        public Task<AccessInfo?> GetAsync() => _loaded ??= LoadAsync();

        public async Task<bool> CanAsync(string permission) => (await GetAsync())?.Can(permission) == true;

        private async Task<AccessInfo?> LoadAsync()
        {
            var principal = _http.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true) return null;
            if (!int.TryParse(principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var userId)) return null;

            await using var db = AppDbContext.ForDatabase(_client.ConnectionString);
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null || user.IsActive != true || !Permissions.CanLogIn(user.Role)) return null;

            var superAdmin = user.Role == Permissions.SuperAdminRole;
            var granted = superAdmin
                ? new HashSet<string>()
                : (await db.UserPermissions.AsNoTracking().Where(p => p.UserId == userId).Select(p => p.Permission).ToListAsync()).ToHashSet();
            return new AccessInfo(user.UserId, user.Username, user.FullName, superAdmin, granted);
        }
    }
}
