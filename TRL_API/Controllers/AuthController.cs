using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
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

        private readonly AppDbContext _context;
        private readonly ITokenService _tokenService;
        private readonly JwtSettings _jwt;

        public AuthController(AppDbContext context, ITokenService tokenService, IConfiguration configuration)
        {
            _context = context;
            _tokenService = tokenService;
            _jwt = configuration.GetSection("JwtSettings").Get<JwtSettings>() ?? new JwtSettings();
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Username) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Username and password are required" });

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                return Unauthorized(new ApiResponse { IsSuccess = false, ErrorMessage = "Invalid username or password" });

            var refreshToken = _tokenService.GenerateRefreshToken();
            _context.RefreshTokens.Add(new RefreshToken
            {
                Token = Hash(refreshToken),
                Expires = DateTime.UtcNow.AddDays(_jwt.RefreshTokenExpirationDays),
                UserId = user.UserId,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            SetAuthCookies(_tokenService.GenerateAccessToken(user), refreshToken);
            return Ok(new ApiResponse { IsSuccess = true, Message = "Login successful" });
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh()
        {
            var refreshToken = Request.Cookies[RefreshCookie];
            if (string.IsNullOrEmpty(refreshToken))
                return Unauthorized(new ApiResponse { IsSuccess = false, ErrorMessage = "Session expired. Please log in again." });

            var hash = Hash(refreshToken);
            var tokenEntity = await _context.RefreshTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.Token == hash);
            if (tokenEntity == null || tokenEntity.IsRevoked || tokenEntity.Expires < DateTime.UtcNow)
                return Unauthorized(new ApiResponse { IsSuccess = false, ErrorMessage = "Session expired. Please log in again." });

            // Remove expired tokens
            _context.RefreshTokens.RemoveRange(_context.RefreshTokens.Where(t => t.Expires < DateTime.UtcNow));

            // Rotate: the presented token stops working and a new one is issued
            var newRefreshToken = _tokenService.GenerateRefreshToken();
            tokenEntity.Token = Hash(newRefreshToken);
            tokenEntity.Expires = DateTime.UtcNow.AddDays(_jwt.RefreshTokenExpirationDays);
            await _context.SaveChangesAsync();

            SetAuthCookies(_tokenService.GenerateAccessToken(tokenEntity.User), newRefreshToken);
            return Ok(new ApiResponse { IsSuccess = true, Message = "Token refreshed successfully" });
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var refreshToken = Request.Cookies[RefreshCookie];
            if (refreshToken != null)
            {
                var hash = Hash(refreshToken);
                var tokenEntity = await _context.RefreshTokens.FirstOrDefaultAsync(t => t.Token == hash);
                if (tokenEntity != null)
                {
                    _context.RefreshTokens.Remove(tokenEntity);
                    await _context.SaveChangesAsync();
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

        // Refresh tokens are stored as SHA-256 hashes, so a copy of the database can't be used to log in
        private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    // DTOs
    public record LoginRequest(string Username, string Password);
}
