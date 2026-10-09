using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TRL_API.BLL;
using TRL_API.Helpers;
using TRL_API.Models;

namespace TRL_API.Controllers
{
    // Security deposits (Rent > Security Deposits): money held for a tenancy, kept apart from rent.
    [Authorize(Roles = "Admin")]
    [Route("api/[controller]")]
    [ApiController]
    public class SecurityDepositsController : ControllerBase
    {
        private readonly ISecurityDepositService _service;
        public SecurityDepositsController(ISecurityDepositService service) => _service = service;

        // Current tenancies, and ended ones that still have a deposit held
        [HttpGet("GetAll")]
        public async Task<IActionResult> GetAll() =>
            Ok(DataTableHelper.ToDictionaryList(await _service.GetTenanciesAsync(), true));

        [HttpGet("History")]
        public async Task<IActionResult> History([FromQuery] int tenantId, [FromQuery] int unitId)
        {
            if (tenantId <= 0 || unitId <= 0)
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Please choose a tenancy." });
            return Ok(DataTableHelper.ToDictionaryList(await _service.GetHistoryAsync(tenantId, unitId), true));
        }

        [HttpPost("Record")]
        public async Task<IActionResult> Record(RecordDepositRequest req)
        {
            var response = await _service.RecordAsync(req, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpPost("Correct")]
        public async Task<IActionResult> Correct(CorrectDepositRequest req)
        {
            var response = await _service.CorrectAsync(req, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpPost("SetAgreed")]
        public async Task<IActionResult> SetAgreed(SetDepositAgreedRequest req)
        {
            var response = await _service.SetAgreedAsync(req, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        // Data for the printable deposit receipt (Received entries only)
        [HttpGet("Receipt")]
        public async Task<IActionResult> Receipt([FromQuery] int depositId)
        {
            if (depositId <= 0)
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Please choose a deposit." });
            var (receipt, error) = await _service.GetReceiptAsync(depositId);
            if (error != null)
                return NotFound(new ApiResponse { IsSuccess = false, ErrorMessage = error });
            return Ok(DataTableHelper.ToDictionaryList(receipt!, true)[0]);
        }
    }
}
