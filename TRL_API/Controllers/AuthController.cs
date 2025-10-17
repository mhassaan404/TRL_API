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
                return Unauthorized();

            var accessToken = _tokenService.GenerateAccessToken(user);
            var refreshToken = _tokenService.GenerateRefreshToken();

            // Save refresh token
            var refreshTokenEntity = new RefreshToken
            {
                Token = refreshToken,
                Expires = DateTime.UtcNow.AddDays(7),
                UserId = user.UserId
            };
            _context.RefreshTokens.Add(refreshTokenEntity);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken
            });
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
        {
            var tokenEntity = await _context.RefreshTokens.Include(t => t.User)
                .FirstOrDefaultAsync(t => t.Token == request.RefreshToken);

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

            return Ok(new
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken
            });
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] RefreshRequest request)
        {
            // Delete refresh token
            var tokenEntity = await _context.RefreshTokens.FirstOrDefaultAsync(t => t.Token == request.RefreshToken);
            if (tokenEntity != null)
            {
                _context.RefreshTokens.Remove(tokenEntity);
                await _context.SaveChangesAsync();
            }

            return Ok();
        }
    }

    // DTOs
    public record LoginRequest(string Username, string Password);
    public record RefreshRequest(string RefreshToken);
}