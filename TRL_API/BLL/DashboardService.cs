using System.Data;
using TRL_API.DAL;

namespace TRL_API.BLL
{
    public class DashboardService : IDashboardService
    {
        private readonly IDashboardRepository _dal;
        public DashboardService(IDashboardRepository dal)
        {
            _dal = dal;
        }

        public async Task<DataTable> GetDashboardData()
        {
            return await _dal.GetDashboardData();
        }
    }
}
