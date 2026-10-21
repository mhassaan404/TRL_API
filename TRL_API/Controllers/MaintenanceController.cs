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
    public class MaintenanceController : ControllerBase
    {
        private readonly IMaintenanceService _service;
        public MaintenanceController(IMaintenanceService service) => _service = service;

        [HttpGet("GetAll")]
        [RequirePermission("maintenance:view")]
        public async Task<IActionResult> GetAll() => Ok(DataTableHelper.ToDictionaryList(await _service.GetAllAsync(), true));

        [HttpGet("GetLog")]
        [RequirePermission("maintenance:view")]
        public async Task<IActionResult> GetLog([FromQuery] int jobId) =>
            Ok(DataTableHelper.ToDictionaryList(await _service.GetLogAsync(jobId), true));

        // Buildings and units (with the unit's current tenant) for the job form
        [HttpGet("GetLocations")]
        [RequirePermission("maintenance:view")]
        public async Task<IActionResult> GetLocations() => Ok(new
        {
            buildings = DataTableHelper.ToDictionaryList(await _service.GetBuildingsAsync(), true),
            units = DataTableHelper.ToDictionaryList(await _service.GetUnitsAsync(), true),
        });

        [HttpPost("Save")]
        [RequirePermission("maintenance:edit")]
        public async Task<IActionResult> Save(MaintenanceJobRequest req)
        {
            var response = await _service.SaveAsync(req, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpPost("ChangeStatus")]
        [RequirePermission("maintenance:edit")]
        public async Task<IActionResult> ChangeStatus(ChangeMaintenanceStatusRequest req)
        {
            var response = await _service.ChangeStatusAsync(req, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpPost("Bill")]
        [RequirePermission("maintenance:edit")]
        public async Task<IActionResult> Bill(BillMaintenanceRequest req)
        {
            var response = await _service.BillAsync(req, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }
    }
}
