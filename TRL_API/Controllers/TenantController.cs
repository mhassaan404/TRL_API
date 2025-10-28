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

        [HttpGet("Tenants")]
        public async Task<IActionResult> GetTenants()
        {
            var data = await _service.GetTenants();
            var list = DataTableHelper.ToDictionaryList(data);
            return Ok(list);
        }

        [HttpGet("Buildings")]
        public async Task<IActionResult> GetBuildings()
        {
            var data = await _service.GetBuildings();
            var list = DataTableHelper.ToDictionaryList(data);
            return Ok(list);
        }

        [HttpGet("Floors")]
        public async Task<IActionResult> GetFloors(int? buildingId)
        {
            var data = await _service.GetFloors(buildingId);
            var list = DataTableHelper.ToDictionaryList(data);
            return Ok(list);
        }

        [HttpGet("Units")]
        public async Task<IActionResult> GetUnits(int? floorId)
        {
            var data = await _service.GetUnits(floorId);
            var list = DataTableHelper.ToDictionaryList(data);
            return Ok(list);
        }

        [HttpPost("SaveTenants")]
        public async Task<IActionResult> SaveTenants(Tenants tenant)
        {
            //tenant.CreatedBy = User.Identity?.Name?.ToString();
            tenant.CreatedBy = User.GetUserId();
            var response = await _service.SaveTenantAsync(tenant);
            return Ok(response);
        }

        [HttpPut("UpdateTenants")]
        public async Task<IActionResult> UpdateTenantAsync(Tenants tenant)
        {
            tenant.UpdatedBy = User.GetUserId();
            var response = await _service.UpdateTenantAsync(tenant);
            return Ok(response);
        }

        [HttpDelete("DeleteTenants")]
        public async Task<IActionResult> DeleteTenantAsync(int tenantId)
        {
            var response = await _service.DeleteTenantAsync(tenantId);
            return Ok(response);
        }
    }
}
