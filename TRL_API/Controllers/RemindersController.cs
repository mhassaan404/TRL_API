using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TRL_API.BLL;
using TRL_API.Helpers;

namespace TRL_API.Controllers
{
    // Payment reminders: read only. The admin sends each reminder from WhatsApp; nothing is stored.
    [Authorize(Roles = "Admin")]
    [Route("api/[controller]")]
    [ApiController]
    public class RemindersController : ControllerBase
    {
        private readonly IReminderService _service;
        public RemindersController(IReminderService service) => _service = service;

        [HttpGet("GetUnpaid")]
        public async Task<IActionResult> GetUnpaid() =>
            Ok(DataTableHelper.ToDictionaryList(await _service.GetUnpaidAsync(), true));
    }
}
