using TRL_API.Models;

namespace TRL_API.BLL
{
    public interface ICompanyProfileService
    {
        Task<CompanyProfile> GetAsync();
        Task<ApiResponse> SaveAsync(SaveCompanyProfileRequest req, int userId);
    }
}
