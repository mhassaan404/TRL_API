using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TRL_API.Data;
using TRL_API.Helpers;
using TRL_API.Models;

namespace TRL_API.Controllers
{
    // Users page (Super Admin only): staff users, their pages (view / edit) and risky actions, password resets.
    // The Super Admin account itself is never changed here (it always has everything and can't be switched off).
    // Users are never deleted, only switched off, so their name stays on everything they recorded.
    [Authorize]
    [SuperAdminOnly]
    [Route("api/[controller]")]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private static readonly Regex UsernameRule = new(@"^[A-Za-z0-9._@-]{3,50}$");
        private const string NotStaff = "This user can't be changed here.";

        private readonly IClientContext _client;
        private readonly ILogger<UsersController> _logger;

        public UsersController(IClientContext client, ILogger<UsersController> logger)
        {
            _client = client;
            _logger = logger;
        }

        private AppDbContext Db() => AppDbContext.ForDatabase(_client.ConnectionString);

        [HttpGet("GetAll")]
        public async Task<IActionResult> GetAll()
        {
            await using var db = Db();
            var users = await db.Users.AsNoTracking()
                .Where(u => u.Role == Permissions.SuperAdminRole || u.Role == Permissions.StaffRole)
                .OrderBy(u => u.Role == Permissions.SuperAdminRole ? 0 : 1).ThenBy(u => u.Username).ToListAsync();
            var perms = (await db.UserPermissions.AsNoTracking().ToListAsync())
                .GroupBy(p => p.UserId).ToDictionary(g => g.Key, g => g.Select(p => p.Permission).OrderBy(p => p).ToList());
            return Ok(users.Select(u => new
            {
                userId = u.UserId,
                username = u.Username,
                fullName = u.FullName,
                email = u.Email,
                phone = u.Phone,
                isSuperAdmin = u.Role == Permissions.SuperAdminRole,
                isActive = u.IsActive == true,
                createdAt = u.CreatedAt,
                permissions = perms.TryGetValue(u.UserId, out var p) ? p : new List<string>(),
            }));
        }

        [HttpPost("Create")]
        public async Task<IActionResult> Create(CreateUserRequest req)
        {
            var username = req?.Username?.Trim() ?? "";
            if (!UsernameRule.IsMatch(username))
                return Fail("Username must be 3–50 characters: letters, numbers, dot, dash, underscore or @ (no spaces).");
            var error = Details(req!.FullName, req.Email, req.Phone) ?? PasswordPolicy.ValidateNew(req.Password, req.ConfirmPassword, username);
            if (error != null) return Fail(error);

            await using var db = Db();
            if (await db.Users.AnyAsync(u => u.Username == username))
                return Fail("This username is already taken.");
            var email = Clean(req.Email);
            if (email != null && await db.Users.AnyAsync(u => u.Email == email))
                return Fail("This email is already used by another user.");

            var user = new User
            {
                Username = username,
                FullName = Clean(req.FullName),
                Email = email,
                Phone = Clean(req.Phone),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
                Role = Permissions.StaffRole,
                IsActive = true,
                CreatedAt = DateTime.Now,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.UserPermissions.AddRange(Permissions.Normalize(req.Permissions).Select(p => new UserPermission { UserId = user.UserId, Permission = p }));
            await db.SaveChangesAsync();

            _logger.LogWarning("User {User} created in {Client} by {By}.", username, _client.Client.ClientCode, User.Identity?.Name);
            return Ok(new ApiResponse { IsSuccess = true, Message = $"User {username} created." });
        }

        // Name, contact, on/off and permissions. Switching a user off signs them out everywhere at once; a permission
        // change applies to their next action.
        [HttpPost("Update")]
        public async Task<IActionResult> Update(UpdateUserRequest req)
        {
            if (req == null) return Fail("Invalid input.");
            var error = Details(req.FullName, req.Email, req.Phone);
            if (error != null) return Fail(error);

            await using var db = Db();
            var user = await db.Users.FirstOrDefaultAsync(u => u.UserId == req.UserId);
            if (user == null || user.Role != Permissions.StaffRole) return Fail(NotStaff);
            var email = Clean(req.Email);
            if (email != null && await db.Users.AnyAsync(u => u.Email == email && u.UserId != user.UserId))
                return Fail("This email is already used by another user.");

            user.FullName = Clean(req.FullName);
            user.Email = email;
            user.Phone = Clean(req.Phone);
            var switchedOff = user.IsActive == true && !req.IsActive;
            user.IsActive = req.IsActive;
            db.UserPermissions.RemoveRange(db.UserPermissions.Where(p => p.UserId == user.UserId));
            db.UserPermissions.AddRange(Permissions.Normalize(req.Permissions).Select(p => new UserPermission { UserId = user.UserId, Permission = p }));
            if (!req.IsActive) db.RefreshTokens.RemoveRange(db.RefreshTokens.Where(t => t.UserId == user.UserId));
            await db.SaveChangesAsync();

            _logger.LogWarning("User {User} in {Client} updated by {By}{Off}.", user.Username, _client.Client.ClientCode, User.Identity?.Name,
                switchedOff ? " (switched off)" : "");
            return Ok(new ApiResponse { IsSuccess = true, Message = $"User {user.Username} saved." });
        }

        // A new password for a staff user (they forgot theirs). Their other sessions are signed out.
        [HttpPost("ResetPassword")]
        public async Task<IActionResult> ResetPassword(ResetUserPasswordRequest req)
        {
            if (req == null) return Fail("Invalid input.");
            await using var db = Db();
            var user = await db.Users.FirstOrDefaultAsync(u => u.UserId == req.UserId);
            if (user == null || user.Role != Permissions.StaffRole) return Fail(NotStaff);
            var error = PasswordPolicy.ValidateNew(req.NewPassword, req.ConfirmPassword, user.Username);
            if (error != null) return Fail(error);

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
            db.RefreshTokens.RemoveRange(db.RefreshTokens.Where(t => t.UserId == user.UserId));
            await db.SaveChangesAsync();

            _logger.LogWarning("Password of {User} in {Client} reset by {By}; their sessions were signed out.", user.Username, _client.Client.ClientCode, User.Identity?.Name);
            return Ok(new ApiResponse { IsSuccess = true, Message = $"Password of {user.Username} changed. They must log in again with the new password." });
        }

        private static string? Details(string? fullName, string? email, string? phone)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return "Please enter the person's name.";
            if (fullName.Trim().Length > 100) return "Name can be at most 100 characters.";
            var e = Clean(email);
            if (e != null && (e.Length > 100 || !Regex.IsMatch(e, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))) return "Please enter a valid email (or leave it empty).";
            var p = Clean(phone);
            if (p != null && (p.Length > 20 || !Regex.IsMatch(p, @"^[0-9+\-() ]+$"))) return "Please enter a valid phone number (or leave it empty).";
            return null;
        }

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private BadRequestObjectResult Fail(string message) =>
            BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = message });
    }

    public record CreateUserRequest(string? Username, string? FullName, string? Email, string? Phone, string? Password, string? ConfirmPassword, List<string>? Permissions);
    public record UpdateUserRequest(int UserId, string? FullName, string? Email, string? Phone, bool IsActive, List<string>? Permissions);
    public record ResetUserPasswordRequest(int UserId, string? NewPassword, string? ConfirmPassword);
}
