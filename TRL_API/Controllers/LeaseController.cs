using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TRL_API.BLL;
using TRL_API.Helpers;
using TRL_API.Models;

namespace TRL_API.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class LeaseController : ControllerBase
    {
        private readonly ILeaseService _service;
        public LeaseController(ILeaseService service) => _service = service;

        [HttpGet("GetAll")]
        [RequirePermission("leases:view", "occupancy:view")]
        public async Task<IActionResult> GetAll() => Ok(DataTableHelper.ToDictionaryList(await _service.GetAllAsync(), true));

        [HttpGet("GetByTenant")]
        [RequirePermission("leases:view")]
        public async Task<IActionResult> GetByTenant([FromQuery] int tenantId) =>
            Ok(DataTableHelper.ToDictionaryList(await _service.GetByTenantAsync(tenantId), true));

        [HttpPost("Create")]
        [RequirePermission("leases:edit")]
        public async Task<IActionResult> Create(Lease lease)
        {
            var response = await _service.CreateAsync(lease, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        // Correct an open lease's start date, rent or tenure (rules in LeaseRepository.UpdateAsync)
        [HttpPost("Update")]
        [RequirePermission("leases:edit")]
        public async Task<IActionResult> Update(UpdateLeaseRequest req)
        {
            var response = await _service.UpdateAsync(req);
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpPost("Renew")]
        [RequirePermission("leases:edit")]
        public async Task<IActionResult> Renew(RenewLeaseRequest req)
        {
            var response = await _service.RenewAsync(req, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpPost("CancelRenewal")]
        [RequirePermission("leases:edit")]
        [RequirePermission("action:deleteCancel")]
        public async Task<IActionResult> CancelRenewal(CancelRenewalRequest req)
        {
            var response = await _service.CancelRenewalAsync(req, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpPost("Cancel")]
        [RequirePermission("leases:edit")]
        [RequirePermission("action:deleteCancel")]
        public async Task<IActionResult> Cancel(CancelLeaseRequest req)
        {
            var response = await _service.CancelLeaseAsync(req, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpPost("Terminate")]
        [RequirePermission("leases:edit")]
        [RequirePermission("action:endLease")]
        public async Task<IActionResult> Terminate(TerminateLeaseRequest req)
        {
            var response = await _service.TerminateAsync(req, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }
    }
}
