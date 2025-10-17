using System.Data;
using TRL_API.DAL;

namespace TRL_API.BLL
{
    public class DashboardService
    {
        private readonly DashboardRepository _dal;
        public DashboardService(DashboardRepository dal)
        {
            _dal = dal;
        }

        public async Task<DataTable> GetDashboardData()
        {
            return await _dal.GetDashboardData();
        }
    }
}
