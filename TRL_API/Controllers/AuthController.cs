using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TRL_API.Data;
using TRL_API.Models;
using TRL_API.Services;

namespace TRL_API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ITokenService _tokenService;

        public AuthController(AppDbContext context, ITokenService tokenService)
        {
            _context = context;
            _tokenService = tokenService;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                return Unauthorized(new { message = "Invalid username or password" });

            var accessToken = _tokenService.GenerateAccessToken(user);
            var refreshToken = _tokenService.GenerateRefreshToken();

            // Save refresh token
            var refreshTokenEntity = new RefreshToken
            {
                Token = refreshToken,
                Expires = DateTime.UtcNow.AddDays(7),
                UserId = user.UserId,
                CreatedAt = DateTime.UtcNow
            };
            _context.RefreshTokens.Add(refreshTokenEntity);
            await _context.SaveChangesAsync();

            // ✅ Set HttpOnly cookies
            Response.Cookies.Append("jwt", accessToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Expires = DateTime.UtcNow.AddMinutes(15)
            });

            Response.Cookies.Append("refreshToken", refreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Expires = DateTime.UtcNow.AddDays(5)
            });

            return Ok(new
            {
                success = true,
                message = "Login successful",
            });
        }

        //[HttpPost("login")]
        //public async Task<IActionResult> Login([FromBody] LoginRequest request)
        //{
        //    // Input validation
        //    if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        //    {
        //        return BadRequest(new { message = "Username and password are required" });
        //    }

        //    var user = await _context.Users
        //        .FirstOrDefaultAsync(u => u.Username == request.Username);

        //    if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        //    {
        //        return Unauthorized(new
        //        {
        //            success = false,
        //            message = "Invalid username or password"
        //        });
        //    }

        //    // Generate tokens
        //    var accessToken = _tokenService.GenerateAccessToken(user);
        //    var refreshToken = _tokenService.GenerateRefreshToken();

        //    // Save new refresh token
        //    var refreshTokenEntity = new RefreshToken
        //    {
        //        Token = refreshToken,
        //        Expires = DateTime.UtcNow.AddDays(7),
        //        UserId = user.UserId,
        //        CreatedAt = DateTime.UtcNow
        //    };
        //    _context.RefreshTokens.Add(refreshTokenEntity);

        //    await _context.SaveChangesAsync();

        //    // Set HttpOnly cookies
        //    Response.Cookies.Append("accessToken", accessToken, new CookieOptions
        //    {
        //        HttpOnly = true,
        //        Secure = true,
        //        SameSite = SameSiteMode.Strict, // Better than None if possible
        //        Expires = DateTime.UtcNow.AddMinutes(15)
        //    });

        //    Response.Cookies.Append("refreshToken", refreshToken, new CookieOptions
        //    {
        //        HttpOnly = true,
        //        Secure = true,
        //        SameSite = SameSiteMode.Strict,
        //        Expires = DateTime.UtcNow.AddDays(7)
        //    });

        //    // Return standardized success response with user info
        //    return Ok(new
        //    {
        //        success = true,
        //        message = "Login successful",
        //        //user = new
        //        //{
        //        //    id = user.UserId,
        //        //    username = user.Username,
        //        //}
        //    });
        //}

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh()
        {
            // ✅ Read refresh token from HttpOnly cookie
            var refreshToken = Request.Cookies["refreshToken"];
            if (string.IsNullOrEmpty(refreshToken))
                return Unauthorized();

            var tokenEntity = await _context.RefreshTokens
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.Token == refreshToken);

            if (tokenEntity == null || tokenEntity.Expires < DateTime.UtcNow)
                return Unauthorized();

            // Remove expired tokens
            var expiredTokens = _context.RefreshTokens.Where(t => t.Expires < DateTime.UtcNow);
            _context.RefreshTokens.RemoveRange(expiredTokens);

            var newAccessToken = _tokenService.GenerateAccessToken(tokenEntity.User);
            var newRefreshToken = _tokenService.GenerateRefreshToken();

            // Update refresh token
            tokenEntity.Token = newRefreshToken;
            tokenEntity.Expires = DateTime.UtcNow.AddDays(7);
            await _context.SaveChangesAsync();

            // ✅ Update cookies
            Response.Cookies.Append("jwt", newAccessToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Expires = DateTime.UtcNow.AddHours(1)
            });

            Response.Cookies.Append("refreshToken", newRefreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Expires = DateTime.UtcNow.AddDays(7)
            });

            return Ok(new { message = "Token refreshed successfully" });
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var refreshToken = Request.Cookies["refreshToken"];
            if (refreshToken != null)
            {
                var tokenEntity = await _context.RefreshTokens.FirstOrDefaultAsync(t => t.Token == refreshToken);
                if (tokenEntity != null)
                {
                    _context.RefreshTokens.Remove(tokenEntity);
                    await _context.SaveChangesAsync();
                }
            }

            // Must match original cookie settings
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Path = "/"
            };

            // Delete cookies correctly
            Response.Cookies.Delete("jwt", cookieOptions);
            Response.Cookies.Delete("refreshToken", cookieOptions);

            return Ok(new { message = "Logged out successfully" });

        }
    }

    // DTOs
    public record LoginRequest(string Username, string Password);
}
