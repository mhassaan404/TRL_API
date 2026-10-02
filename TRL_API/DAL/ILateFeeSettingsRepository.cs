using TRL_API.Models;

namespace TRL_API.DAL
{
    public interface ILateFeeSettingsRepository
    {
        Task<LateFeeSettings> GetAsync();
        Task<ApiResponse> SaveAsync(int paymentDueDays, decimal lateFeePerDay, decimal maxLateFeeMultiplier, int userId);
    }
}
