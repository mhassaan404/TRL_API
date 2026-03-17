using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TRL_API.BLL;
using TRL_API.Helpers;

namespace TRL_API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RentHistoryController : ControllerBase
    {
        private readonly RentHistoryService _service;

        public RentHistoryController(RentHistoryService service)
        {
            _service = service;
        }

        // GetHistory
        [HttpGet("History")]
        public async Task<IActionResult> GetHistoryAsync()
        {
            var data = await _service.GetHistoryAsync();
            var list = DataTableHelper.ToDictionaryList(data, true);
            return Ok(list);
        }
    }
}
