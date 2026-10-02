using TRL_API.Models;

namespace TRL_API.BLL
{
    public interface ILateFeeSettingsService
    {
        Task<LateFeeSettings> GetAsync();
        Task<ApiResponse> SaveAsync(SaveLateFeeSettingsRequest req, int userId);
    }
}
