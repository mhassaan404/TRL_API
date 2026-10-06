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
    public class RentHistoryController : ControllerBase
    {
        private readonly IRentHistoryService _service;

        public RentHistoryController(IRentHistoryService service)
        {
            _service = service;
        }

        // GetHistory
        [HttpGet("History")]
        public async Task<IActionResult> GetHistoryAsync()
        {
            var data = await _service.GetHistoryAsync();
            var list = DataTableHelper.ToDictionaryList(data, true);
            return Ok(list);
        }

        // One invoice with its payments and recorded events (History window on Rent History). Read only.
        [HttpGet("InvoiceDetails")]
        public async Task<IActionResult> GetInvoiceDetails([FromQuery] int invoiceId)
        {
            if (invoiceId <= 0)
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Invoice is required." });

            var (invoice, payments, events, charges) = await _service.GetInvoiceDetailsAsync(invoiceId);
            if (invoice.Rows.Count == 0)
                return NotFound(new ApiResponse { IsSuccess = false, ErrorMessage = "Invoice not found." });

            return Ok(new
            {
                invoice = DataTableHelper.ToDictionaryList(invoice, true)[0],
                payments = DataTableHelper.ToDictionaryList(payments, true),
                events = DataTableHelper.ToDictionaryList(events, true),
                linkedCharges = DataTableHelper.ToDictionaryList(charges, true),
            });
        }

        [Authorize(Roles = "Admin")]
        [HttpPatch("CancelInvoice")]
        public async Task<IActionResult> CancelInvoice([FromBody] int id, [FromQuery] string? reason)
        {
            if (id <= 0) return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Invoice is required." });
            return Ok(await _service.CancelInvoice(id, reason, User.GetUserId()));
        }

        [Authorize(Roles = "Admin")]
        [HttpPatch("ReinstateInvoice")]
        public async Task<IActionResult> ReinstateInvoice([FromBody] int id)
        {
            if (id <= 0)
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Invoice is required." });

            var response = await _service.ReinstateInvoice(id);
            return Ok(response);
        }
    }
}
