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
    public class TenantController : ControllerBase
    {
        private readonly ITenantService _service;
        public TenantController(ITenantService service)
        {
            _service = service;
        }

        [HttpGet("GetTenants")]
        [RequirePermission("tenants:view")]
        public async Task<IActionResult> GetTenants()
        {
            var data = await _service.GetTenants();
            var list = DataTableHelper.ToDictionaryList(data, true);
            return Ok(list);
        }

        [HttpPost("Create")]
        [RequirePermission("tenants:edit")]
        public async Task<IActionResult> CreateTenant(Tenants tenant)
        {
            if (tenant == null)
                return BadRequest(new ApiResponse { IsSuccess = false, Message = "Tenant data is required." });

            tenant.CreatedBy = User.GetUserId();
            var response = await _service.SaveTenantAsync(tenant);

            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpPut("Update")]
        [RequirePermission("tenants:edit")]
        public async Task<IActionResult> UpdateTenant(Tenants tenant)
        {
            if (tenant == null)
                return BadRequest(new ApiResponse { IsSuccess = false, Message = "Tenant data is required." });

            tenant.UpdatedBy = User.GetUserId();
            var response = await _service.UpdateTenantAsync(tenant);

            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpDelete("Delete")]
        [RequirePermission("tenants:edit")]
        [RequirePermission("action:deleteCancel")]
        public async Task<IActionResult> DeleteTenant([FromQuery] int tenantId)
        {
            if (tenantId <= 0)
                return BadRequest(new ApiResponse { IsSuccess = false, Message = "TenantId is required." });

            var response = await _service.DeleteTenantAsync(tenantId, User.GetUserId());

            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

    }
}
