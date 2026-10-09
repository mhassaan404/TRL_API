using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TRL_API.BLL;
using TRL_API.Helpers;
using TRL_API.Models;

namespace TRL_API.Controllers
{
    // Move-out settlements (Rent > Move-out Settlements)
    [Authorize(Roles = "Admin")]
    [Route("api/[controller]")]
    [ApiController]
    public class SettlementsController : ControllerBase
    {
        private readonly ISettlementService _service;
        public SettlementsController(ISettlementService service) => _service = service;

        // Ended tenancies waiting for settlement, and settlements already recorded
        [HttpGet("GetAll")]
        public async Task<IActionResult> GetAll() => Ok(new
        {
            candidates = DataTableHelper.ToDictionaryList(await _service.GetCandidatesAsync(), true),
            settlements = DataTableHelper.ToDictionaryList(await _service.GetSettlementsAsync(), true),
        });

        // What would be settled now: tenancy, open invoices, deposit, rent months without an invoice
        [HttpGet("Preview")]
        public async Task<IActionResult> Preview([FromQuery] int tenantId, [FromQuery] int unitId)
        {
            if (tenantId <= 0 || unitId <= 0)
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Please choose the tenancy to settle." });
            var p = await _service.GetPreviewAsync(tenantId, unitId);
            if (p == null)
                return NotFound(new ApiResponse { IsSuccess = false, ErrorMessage = "No lease found for this tenant and unit." });
            var (header, invoices, unbilled) = p.Value;
            return Ok(new
            {
                header = DataTableHelper.ToDictionaryList(header, true)[0],
                invoices = DataTableHelper.ToDictionaryList(invoices, true),
                unbilled = DataTableHelper.ToDictionaryList(unbilled, true),
            });
        }

        [HttpPost("Finalize")]
        public async Task<IActionResult> Finalize(FinalizeSettlementRequest req)
        {
            var response = await _service.FinalizeAsync(req, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        // A refund paid after the settlement was recorded
        [HttpPost("RecordRefund")]
        public async Task<IActionResult> RecordRefund(RecordSettlementRefundRequest req)
        {
            var response = await _service.RecordRefundAsync(req, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        // Statement data
        [HttpGet("Get")]
        public async Task<IActionResult> Get([FromQuery] int id)
        {
            if (id <= 0)
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Please choose a settlement." });
            var s = await _service.GetSettlementAsync(id);
            if (s == null)
                return NotFound(new ApiResponse { IsSuccess = false, ErrorMessage = $"Settlement #{id} was not found." });
            var (header, lines, refunds) = s.Value;
            return Ok(new
            {
                settlement = DataTableHelper.ToDictionaryList(header, true)[0],
                lines = DataTableHelper.ToDictionaryList(lines, true),
                refunds = DataTableHelper.ToDictionaryList(refunds, true),
            });
        }
    }
}
