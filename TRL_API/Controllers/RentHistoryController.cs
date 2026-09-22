using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TRL_API.BLL;
using TRL_API.Helpers;
using TRL_API.Models;

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

        [HttpPatch("CancelInvoice")]
        public async Task<IActionResult> CancelInvoice([FromBody] int id, [FromQuery] string? reason)
        {
            if (id <= 0) return Ok(new ApiResponse { IsSuccess = false, ErrorMessage = "Invoice Id not found." });
            return Ok(await _service.CancelInvoice(id, reason, 1)); // TODO: real user id when security is added
        }

        [HttpPatch("ReinstateInvoice")]
        public async Task<IActionResult> ReinstateInvoice([FromBody] int id)
        {
            if (id <= 0)
                return Ok("Invoice Id not found.");

            var response = await _service.ReinstateInvoice(id);
            return Ok(response);
        }
    }
}
