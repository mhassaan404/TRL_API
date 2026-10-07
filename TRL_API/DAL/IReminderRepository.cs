using System.Data;

namespace TRL_API.DAL
{
    public interface IReminderRepository
    {
        Task<DataTable> GetUnpaidAsync();
    }
}
