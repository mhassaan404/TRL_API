using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
            var data = await _service.GetFloors(buildingId);
            var list = DataTableHelper.ToDictionaryList(data);
            return Ok(list);
        }

        [HttpGet("GetUnitsByFloor")]
        public async Task<IActionResult> GetUnitsByFloor([FromQuery] int? floorId)
        {
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

        [HttpPost("Create")]
        public async Task<IActionResult> CreateTenant(Tenants tenant)
        {
            //tenant.CreatedBy = User.Identity?.Name?.ToString();
            tenant.CreatedBy = User.GetUserId();
            var response = await _service.SaveTenantAsync(tenant);
            return Ok(response);
        }

        [HttpPut("Update")]
        public async Task<IActionResult> UpdateTenant(Tenants tenant)
        {
            tenant.UpdatedBy = User.GetUserId();
            var response = await _service.UpdateTenantAsync(tenant);
            return Ok(response);
        }

        [HttpDelete("Delete")]
        public async Task<IActionResult> DeleteTenant([FromQuery] int tenantId)
        {
            var response = await _service.DeleteTenantAsync(tenantId);
            return Ok(response);
        }
    }
}
