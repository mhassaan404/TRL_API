using System.Data;
using TRL_API.Models;

namespace TRL_API.DAL
{
    public interface ICompanyProfileRepository
    {
        Task<DataTable> GetAsync();
        Task<ApiResponse> SaveAsync(SaveCompanyProfileRequest p, LogoChange logo, byte[]? logoBytes, string? logoType, int userId);
    }

    public enum LogoChange { Keep, Replace, Remove }
}
