using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TRL_API.BLL;
using TRL_API.Helpers;
using TRL_API.Models;

namespace TRL_API.Controllers
{
    [Authorize(Roles = "Admin")]
    [Route("api/[controller]")]
    [ApiController]
    public class LeaseController : ControllerBase
    {
        private readonly ILeaseService _service;
        public LeaseController(ILeaseService service) => _service = service;

        [HttpGet("GetAll")]
        public async Task<IActionResult> GetAll() => Ok(DataTableHelper.ToDictionaryList(await _service.GetAllAsync(), true));

        [HttpGet("GetByTenant")]
        public async Task<IActionResult> GetByTenant([FromQuery] int tenantId) =>
            Ok(DataTableHelper.ToDictionaryList(await _service.GetByTenantAsync(tenantId), true));

        [Authorize(Roles = "Admin")]
        [HttpPost("Create")]
        public async Task<IActionResult> Create(Lease lease)
        {
            var response = await _service.CreateAsync(lease, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("Renew")]
        public async Task<IActionResult> Renew(RenewLeaseRequest req)
        {
            var response = await _service.RenewAsync(req, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("CancelRenewal")]
        public async Task<IActionResult> CancelRenewal(CancelRenewalRequest req)
        {
            var response = await _service.CancelRenewalAsync(req, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("Terminate")]
        public async Task<IActionResult> Terminate(TerminateLeaseRequest req)
        {
            var response = await _service.TerminateAsync(req, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }
    }
}
