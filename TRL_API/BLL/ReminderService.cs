using System.Data;
using TRL_API.DAL;

namespace TRL_API.BLL
{
    public class ReminderService : IReminderService
    {
        private readonly IReminderRepository _dal;
        public ReminderService(IReminderRepository dal) => _dal = dal;

        public async Task<DataTable> GetUnpaidAsync() => await _dal.GetUnpaidAsync();
    }
}
