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
        private readonly ITenantService _service;
        public TenantController(ITenantService service)
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

            var response = await _service.DeleteTenantAsync(tenantId, User.GetUserId());

            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

    }
}
