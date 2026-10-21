using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TRL_API.BLL;
using TRL_API.Helpers;
using TRL_API.Models;

namespace TRL_API.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class LateFeeSettingsController : ControllerBase
    {
        private readonly ILateFeeSettingsService _service;
        public LateFeeSettingsController(ILateFeeSettingsService service) => _service = service;

        [HttpGet("Get")]
        [RequirePermission("lateFeeSettings:view")]
        public async Task<IActionResult> Get() => Ok(await _service.GetAsync());

        [HttpPost("Save")]
        [RequirePermission("lateFeeSettings:edit")]
        public async Task<IActionResult> Save(SaveLateFeeSettingsRequest req)
        {
            var response = await _service.SaveAsync(req, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }
    }
}
