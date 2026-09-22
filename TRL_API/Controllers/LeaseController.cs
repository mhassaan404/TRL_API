using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TRL_API.BLL;
using TRL_API.Helpers;
using TRL_API.Models;

namespace TRL_API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LeaseController : ControllerBase
    {
        private readonly LeaseService _service;
        public LeaseController(LeaseService service) => _service = service;

        [HttpGet("GetAll")]
        public async Task<IActionResult> GetAll() => Ok(DataTableHelper.ToDictionaryList(await _service.GetAllAsync(), true));

        [HttpGet("GetByTenant")]
        public async Task<IActionResult> GetByTenant([FromQuery] int tenantId) =>
            Ok(DataTableHelper.ToDictionaryList(await _service.GetByTenantAsync(tenantId), true));

        [HttpPost("Create")]
        public async Task<IActionResult> Create(Lease lease)
        {
            var response = await _service.CreateAsync(lease, 1); // TODO: real user id when security is added
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpPost("Renew")]
        public async Task<IActionResult> Renew(RenewLeaseRequest req)
        {
            var response = await _service.RenewAsync(req, 1);
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpPost("Terminate")]
        public async Task<IActionResult> Terminate(TerminateLeaseRequest req)
        {
            var response = await _service.TerminateAsync(req);
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }
    }
}
