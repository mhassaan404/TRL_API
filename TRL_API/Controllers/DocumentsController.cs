using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TRL_API.BLL;
using TRL_API.Helpers;
using TRL_API.Models;

namespace TRL_API.Controllers
{
    // Data for printable payment receipts and invoices: read only.
    [Authorize(Roles = "Admin")]
    [Route("api/[controller]")]
    [ApiController]
    public class DocumentsController : ControllerBase
    {
        private readonly IDocumentService _service;
        public DocumentsController(IDocumentService service) => _service = service;

        // ids = one or more payment Ids, comma separated (e.g. the payments recorded together)
        [HttpGet("Receipts")]
        public async Task<IActionResult> Receipts([FromQuery] string? ids)
        {
            var (list, error) = _service.ParseReceiptIds(ids);
            if (error != null)
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = error });

            var (receipts, notFound) = await _service.GetReceiptsAsync(list!);
            if (notFound != null)
                return NotFound(new ApiResponse { IsSuccess = false, ErrorMessage = notFound });
            return Ok(DataTableHelper.ToDictionaryList(receipts!, true));
        }

        [HttpGet("Invoice")]
        public async Task<IActionResult> Invoice([FromQuery] int invoiceId)
        {
            if (invoiceId <= 0)
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Please choose an invoice." });

            var result = await _service.GetInvoiceAsync(invoiceId);
            if (result == null)
                return NotFound(new ApiResponse { IsSuccess = false, ErrorMessage = $"Invoice #{invoiceId} was not found." });

            var (invoice, payments) = result.Value;
            return Ok(new
            {
                invoice = DataTableHelper.ToDictionaryList(invoice, true)[0],
                payments = DataTableHelper.ToDictionaryList(payments, true),
            });
        }
    }
}
