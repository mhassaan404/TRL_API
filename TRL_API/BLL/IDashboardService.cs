using TRL_API.Models;
using System.Data;
using TRL_API.DAL;

namespace TRL_API.BLL
{
    public interface IDashboardService
    {
        Task<DataTable> GetDashboardData();
    }
}
