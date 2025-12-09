using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TRL_API.BLL;
using TRL_API.Helpers;
using TRL_API.Models;

namespace TRL_API.Controllers
{
    [Authorize(Roles = "Admin,Tenant")]
    [Route("api/[controller]")]
    [ApiController]
    public class TenantController : ControllerBase
    {
        private readonly TenantService _service;
        public TenantController(TenantService service)
        {
            _service = service;
        }

        [HttpGet("GetTenants")]
        public async Task<IActionResult> GetTenants()
        {
            var data = await _service.GetTenants();
            var list = DataTableHelper.ToDictionaryList(data);
            return Ok(list);
        }

        [HttpGet("GetBuildings")]
        public async Task<IActionResult> GetBuildings()
        {
            var data = await _service.GetBuildings();
            var list = DataTableHelper.ToDictionaryList(data);
            return Ok(list);
        }

        [HttpGet("GetFloorsByBuilding")]
        public async Task<IActionResult> GetFloorsByBuilding([FromQuery] int? buildingId)
        {
            if (!buildingId.HasValue) return BadRequest("BuildingId is required");

            var data = await _service.GetFloors(buildingId);
            var list = DataTableHelper.ToDictionaryList(data);
            return Ok(list);
        }

        [HttpGet("GetUnitsByFloor")]
        public async Task<IActionResult> GetUnitsByFloor([FromQuery] int? floorId)
        {
            if (!floorId.HasValue) return BadRequest("FloorId is required");

            var data = await _service.GetUnits(floorId);
            var list = DataTableHelper.ToDictionaryList(data);
            return Ok(list);
        }

        [HttpGet("GetCities")]
        public async Task<IActionResult> GetCities()
        {
            var data = await _service.GetCities();
            var list = DataTableHelper.ToDictionaryList(data);
            return Ok(list);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("Create")]
        public async Task<IActionResult> CreateTenant(Tenants tenant)
        {
            if (tenant == null)
                return BadRequest(new ApiResponse { IsSuccess = false, Message = "Tenant data is required." });

            tenant.CreatedBy = User.GetUserId();
            var response = await _service.SaveTenantAsync(tenant);

            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [Authorize(Roles = "Admin")]
        [HttpPut("Update")]
        public async Task<IActionResult> UpdateTenant(Tenants tenant)
        {
            if (tenant == null)
                return BadRequest(new ApiResponse { IsSuccess = false, Message = "Tenant data is required." });

            tenant.UpdatedBy = User.GetUserId();
            var response = await _service.UpdateTenantAsync(tenant);

            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [Authorize(Roles = "Admin")]
        [HttpDelete("Delete")]
        public async Task<IActionResult> DeleteTenant([FromQuery] int tenantId)
        {
            if (tenantId <= 0)
                return BadRequest(new ApiResponse { IsSuccess = false, Message = "TenantId is required." });

            var response = await _service.DeleteTenantAsync(tenantId);

            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

    }
}
