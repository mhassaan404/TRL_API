using System.Data;
using TRL_API.DAL;

namespace TRL_API.BLL
{
    public class PropertiesService
    {
        private readonly PropertiesRepository _dal;
        public PropertiesService(PropertiesRepository dal)
        {
            _dal = dal;
        }

        public async Task<DataTable> GetProperties()
        {
            return await _dal.GetProperties();
        }
    }
}
