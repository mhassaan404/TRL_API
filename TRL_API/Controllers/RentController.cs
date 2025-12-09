using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TRL_API.BLL;
using TRL_API.Helpers;
using TRL_API.Models;

namespace TRL_API.Controllers
{
    //[Authorize(Roles = "Admin,Tenant")]
    [Route("api/[controller]")]
    [ApiController]
    public class RentController : ControllerBase
    {
        private readonly RentService _service;

        public RentController(RentService service)
        {
            _service = service;
        }

        // Tenants
        [HttpGet("GetTenants")]
        public async Task<IActionResult> GetTenants()
        {
            var data = await _service.GetTenantsAsync();
            var list = DataTableHelper.ToDictionaryList(data);
            return Ok(list);
        }

        [HttpGet("GetStatusList")]
        public async Task<IActionResult> GetStatusList()
        {
            var data = await _service.GetStatusListAsync();
            var list = DataTableHelper.ToDictionaryList(data);
            return Ok(list);
        }

        // Invoices
        [HttpGet("GetInvoicesByTenant")]
        public async Task<IActionResult> GetInvoicesByTenant([FromQuery] int tenantId)
        {
            if (tenantId <= 0)
                return Ok("TenantId is required and must be greater than zero.");

            var data = await _service.GetInvoicesByTenantAsync(tenantId);
            var list = DataTableHelper.ToDictionaryList(data);
            return Ok(list);
        }

        [HttpGet("GetInvoiceDetails")]
        public async Task<IActionResult> GetInvoiceDetails([FromQuery] int invoiceId)
        {
            if (invoiceId <= 0)
                return Ok(new ApiResponse { IsSuccess = false, ErrorMessage = "InvoiceId is required and must be greater than zero." });

            var data = await _service.GetInvoiceDetailsAsync(invoiceId);
            var list = DataTableHelper.ToDictionaryList(data);
            return Ok(list);
        }

        // Payments
        [Authorize(Roles = "Admin")]
        [HttpPost("CreateRent")]
        public async Task<IActionResult> CreateRent(Payments payments)
        {
            if (payments.TenantId <= 0 || payments.RentInvoiceId <= 0 || payments.PaymentAmount <= 0)
                return Ok(new ApiResponse { IsSuccess = false, ErrorMessage = "Invalid input parameters." });

            var response = await _service.CreateRentAsync(payments);
            if (response.IsSuccess)
                return Ok(response);

            return Ok(response);
        }

        [HttpGet("GetRentCollection")]
        public async Task<IActionResult> GetRentCollection()
        {
            var data = await _service.GetRentCollectionAsync();
            var list = DataTableHelper.ToDictionaryList(data);
            return Ok(list);
        }

        [HttpGet("GetPaymentHistory")]
        public async Task<IActionResult> GetPaymentHistory([FromQuery] int invoiceId)
        {
            if (invoiceId <= 0)
                return Ok(new ApiResponse { IsSuccess = false, ErrorMessage = "InvoiceId is required and must be greater than zero." });

            var data = await _service.GetPaymentHistoryAsync(invoiceId);
            var list = DataTableHelper.ToDictionaryList(data);
            return Ok(list);
        }

        // Bulk Operations
        [Authorize(Roles = "Admin")]
        [HttpPut("BulkUpdateDueDate")]
        public async Task<IActionResult> BulkUpdateDueDate([FromBody] BulkDueDateUpdateRequest request)
        {
            if (request.InvoiceIds == null || request.InvoiceIds.Count == 0)
                return Ok(new ApiResponse { IsSuccess = false, ErrorMessage = "Invoice list cannot be empty." });

            var response = await _service.BulkUpdateDueDateAsync(request.InvoiceIds, request.NewDueDate);
            if (response.IsSuccess)
                return Ok(response);

            return Ok(response);
        }
    }

    // Helper DTO
    public class BulkDueDateUpdateRequest
    {
        public List<int> InvoiceIds { get; set; } = new();
        public DateTime NewDueDate { get; set; }
    }
}
