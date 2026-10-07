using System.Data;

namespace TRL_API.BLL
{
    public interface IReminderService
    {
        Task<DataTable> GetUnpaidAsync();
    }
}
