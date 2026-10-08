using TRL_API.Models;

namespace TRL_API.Services
{
    public interface ITokenService
    {
        string GenerateAccessToken(User user, int clientId);
        string GenerateRefreshToken();
    }
}
