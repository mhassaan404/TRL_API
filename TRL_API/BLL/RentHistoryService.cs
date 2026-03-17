using System.Data;
using TRL_API.DAL;
using TRL_API.Data;

namespace TRL_API.BLL
{
    public class RentHistoryService
    {
        private readonly DbHelper _dbHelper;
        private readonly RentHistoryRepository _dal;

        public RentHistoryService(RentHistoryRepository dal, DbHelper dbHelper)
        {
            _dal = dal;
            _dbHelper = dbHelper;
        }

        public async Task<DataTable> GetHistoryAsync()
        {
            return await _dal.GetHistoryAsync();
        }
    }
}
