using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TRL_API.Data;
using TRL_API.Models;
using TRL_API.Services;

namespace TRL_API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private const string AccessCookie = "jwt";
        private const string RefreshCookie = "refreshToken";
        private const string InvalidLogin = "Invalid client code, username or password";
        private const string SessionExpired = "Session expired. Please log in again.";

        // A real BCrypt hash no password matches: checked when the client or user doesn't exist, so the response takes
        // as long as a wrong password and doesn't reveal which client codes or usernames exist
        private static readonly string NoUserHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"));

        private readonly IClientCatalog _catalog;
        private readonly ITokenService _tokenService;
        private readonly JwtSettings _jwt;
        private readonly LoginThrottle _throttle;

        // Users and refresh tokens are in each client's database: every action first finds the client (by the code
        // typed at login, or by the client id in the refresh cookie), then works only in that client's database.
        public AuthController(IClientCatalog catalog, ITokenService tokenService, IConfiguration configuration, LoginThrottle throttle)
        {
            _catalog = catalog;
            _tokenService = tokenService;
            _jwt = configuration.GetSection("JwtSettings").Get<JwtSettings>() ?? new JwtSettings();
            _throttle = throttle;
        }

        // Also limited per IP address by the "login" rate-limit policy (Program.cs)
        [HttpPost("login")]
        [EnableRateLimiting("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.ClientCode) || string.IsNullOrWhiteSpace(request.Username)
                || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Client code, username and password are required" });

            var clientCode = request.ClientCode.Trim();

            // Checked before the password, so a locked username can't keep guessing
            var lockedMinutes = _throttle.LockedMinutesLeft(clientCode, request.Username, ClientIp);
            if (lockedMinutes != null)
            {
                var msg = $"Too many failed login attempts. Try again in {lockedMinutes} minute(s).";
                return StatusCode(StatusCodes.Status429TooManyRequests, new ApiResponse { IsSuccess = false, Message = msg, ErrorMessage = msg });
            }

            // Unknown or inactive client, or no database configured: same answer as a wrong password
            var client = await _catalog.GetByCodeAsync(clientCode);
            if (client == null || !client.IsActive || client.ConnectionString == null)
            {
                BCrypt.Net.BCrypt.Verify(request.Password, NoUserHash);
                _throttle.RecordFailure(clientCode, request.Username, ClientIp);
                return Unauthorized(new ApiResponse { IsSuccess = false, ErrorMessage = InvalidLogin });
            }

            await using var db = AppDbContext.ForDatabase(client.ConnectionString);
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
            if (!BCrypt.Net.BCrypt.Verify(request.Password, user?.PasswordHash ?? NoUserHash) || user == null)
            {
                _throttle.RecordFailure(clientCode, request.Username, ClientIp);
                return Unauthorized(new ApiResponse { IsSuccess = false, ErrorMessage = InvalidLogin });
            }
            _throttle.Reset(clientCode, request.Username, ClientIp);

            // Checked only after the password is verified, so it doesn't reveal which accounts exist
            if (user.IsActive != true)
            {
                const string msg = "This account is deactivated. Contact an administrator.";
                return StatusCode(StatusCodes.Status403Forbidden, new ApiResponse { IsSuccess = false, Message = msg, ErrorMessage = msg });
            }
            // Only Admin can use the app until tenant-specific access exists (every controller requires Admin)
            if (user.Role != "Admin")
                return StatusCode(StatusCodes.Status403Forbidden, new ApiResponse { IsSuccess = false, Message = "This account doesn't have access to the application.", ErrorMessage = "This account doesn't have access to the application." });

            // A client database that hasn't been migrated to what this API needs can't be used yet
            if (!await _catalog.IsSchemaCurrentAsync(client))
            {
                const string msg = "This company's database needs an update before it can be used. Please contact support.";
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiResponse { IsSuccess = false, Message = msg, ErrorMessage = msg });
            }

            var refreshToken = _tokenService.GenerateRefreshToken();
            db.RefreshTokens.Add(new RefreshToken
            {
                Token = Hash(refreshToken),
                Expires = DateTime.UtcNow.AddDays(_jwt.RefreshTokenExpirationDays),
                UserId = user.UserId,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            SetAuthCookies(_tokenService.GenerateAccessToken(user, client.ClientId), RefreshCookieValue(client.ClientId, refreshToken));
            // Non-secret details for the screen (the tokens stay in HttpOnly cookies)
            return Ok(new
            {
                isSuccess = true,
                message = "Login successful",
                user = new { username = user.Username, role = user.Role, clientCode = client.ClientCode, clientName = client.ClientName },
            });
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh()
        {
            // The cookie says which client the token belongs to; the token itself is then looked up (as a hash) only in
            // that client's database, so a changed client id simply finds no token
            var client = await ClientOfRefreshCookie();
            if (client.Client == null || !client.Client.IsActive || client.Client.ConnectionString == null
                || !await _catalog.IsSchemaCurrentAsync(client.Client))
                return ExpiredSession();

            await using var db = AppDbContext.ForDatabase(client.Client.ConnectionString);
            var hash = Hash(client.Token!);
            var tokenEntity = await db.RefreshTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.Token == hash);
            if (tokenEntity == null || tokenEntity.IsRevoked || tokenEntity.Expires < DateTime.UtcNow
                || tokenEntity.User.Role != "Admin" || tokenEntity.User.IsActive != true)
                return ExpiredSession();

            // Remove expired tokens
            db.RefreshTokens.RemoveRange(db.RefreshTokens.Where(t => t.Expires < DateTime.UtcNow));

            // Rotate: the presented token stops working and a new one is issued (same client)
            var newRefreshToken = _tokenService.GenerateRefreshToken();
            tokenEntity.Token = Hash(newRefreshToken);
            tokenEntity.Expires = DateTime.UtcNow.AddDays(_jwt.RefreshTokenExpirationDays);
            await db.SaveChangesAsync();

            SetAuthCookies(_tokenService.GenerateAccessToken(tokenEntity.User, client.Client.ClientId),
                RefreshCookieValue(client.Client.ClientId, newRefreshToken));
            return Ok(new ApiResponse { IsSuccess = true, Message = "Token refreshed successfully" });
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var client = await ClientOfRefreshCookie();
            if (client.Client?.ConnectionString != null)
            {
                await using var db = AppDbContext.ForDatabase(client.Client.ConnectionString);
                var hash = Hash(client.Token!);
                var tokenEntity = await db.RefreshTokens.FirstOrDefaultAsync(t => t.Token == hash);
                if (tokenEntity != null)
                {
                    db.RefreshTokens.Remove(tokenEntity);
                    await db.SaveChangesAsync();
                }
            }

            // Must match the options the cookies were set with
            Response.Cookies.Delete(AccessCookie, CookieOptions());
            Response.Cookies.Delete(RefreshCookie, CookieOptions());
            return Ok(new ApiResponse { IsSuccess = true, Message = "Logged out successfully" });
        }

        // Cookie lifetimes match the tokens they carry (JwtSettings), so neither outlives the other.
        private void SetAuthCookies(string accessToken, string refreshToken)
        {
            Response.Cookies.Append(AccessCookie, accessToken, CookieOptions(DateTime.UtcNow.AddMinutes(_jwt.AccessTokenExpirationMinutes)));
            Response.Cookies.Append(RefreshCookie, refreshToken, CookieOptions(DateTime.UtcNow.AddDays(_jwt.RefreshTokenExpirationDays)));
        }

        private static CookieOptions CookieOptions(DateTime? expires = null) => new()
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = "/",
            Expires = expires
        };

        private string ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Refresh cookie = "<clientId>.<token>". The client id only says where to look; the token proves the session.
        private static string RefreshCookieValue(int clientId, string token) => $"{clientId}.{token}";

        // The client and token from the refresh cookie (Client null when missing, malformed or unknown;
        // a cookie from before multi-client has no client id and so ends that session)
        private async Task<(ClientInfo? Client, string? Token)> ClientOfRefreshCookie()
        {
            var value = Request.Cookies[RefreshCookie];
            var dot = value?.IndexOf('.') ?? -1;
            if (value == null || dot <= 0 || dot == value.Length - 1 || !int.TryParse(value[..dot], out var clientId))
                return (null, null);
            return (await _catalog.GetByIdAsync(clientId), value[(dot + 1)..]);
        }

        private UnauthorizedObjectResult ExpiredSession() =>
            Unauthorized(new ApiResponse { IsSuccess = false, ErrorMessage = SessionExpired });

        // Refresh tokens are stored as SHA-256 hashes, so a copy of the database can't be used to log in
        private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    // DTOs
    public record LoginRequest(string ClientCode, string Username, string Password);
}
