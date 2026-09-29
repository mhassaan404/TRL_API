using TRL_API.Models;
using System.Data;
using TRL_API.Data;

namespace TRL_API.DAL
{
    public interface IDashboardRepository
    {
        Task<DataTable> GetDashboardData();
    }
}
