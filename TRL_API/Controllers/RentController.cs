using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using TRL_API.BLL;
using TRL_API.Helpers;
using TRL_API.Models;

namespace TRL_API.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class RentController : ControllerBase
    {
        private readonly IRentService _service;

        public RentController(IRentService service)
        {
            _service = service;
        }

        // Tenants
        [HttpGet("GetTenants")]
        [RequirePermission("rentCollection:view")]
        public async Task<IActionResult> GetTenants()
        {
            var data = await _service.GetTenantsAsync();
            var list = DataTableHelper.ToDictionaryList(data, true);
            return Ok(list);
        }

        // Invoices
        [HttpGet("GetInvoicesByTenant")]
        [RequirePermission("rentCollection:view")]
        public async Task<IActionResult> GetInvoicesByTenant([FromQuery] int tenantId)
        {
            if (tenantId <= 0)
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Tenant is required." });

            var data = await _service.GetInvoicesByTenantAsync(tenantId);
            var list = DataTableHelper.ToDictionaryList(data, true);
            return Ok(list);
        }

        [HttpGet("GetPaymentHistoryById")]
        [RequirePermission("rentCollection:view")]
        public async Task<IActionResult> GetPaymentHistoryById([FromQuery] int invoiceId)
        {
            if (invoiceId <= 0)
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Invoice is required." });
            var data = await _service.GetPaymentHistoryByIdAsync(invoiceId);
            var list = DataTableHelper.ToDictionaryList(data, true);
            return Ok(list);
        }

        [HttpGet("GetUnpaidInvoiceByTenant")]
        [RequirePermission("rentCollection:view")]
        public async Task<IActionResult> GetUnpaidInvoiceByTenant([FromQuery] int tenantId)
        {
            if (tenantId <= 0)
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Tenant is required." });
            var data = await _service.GetUnpaidInvoicesByTenantAsync(tenantId);
            return Ok(data);
        }


        [HttpPost("ReverseLateFee")]
        [RequirePermission("rentCollection:edit")]
        [RequirePermission("action:lateFee")]
        public async Task<IActionResult> ReverseLateFee([FromBody] ReverseLateFeeRequest req)
        {
            var response = await _service.ReverseLateFeeAsync(req.InvoiceId, req.Reason, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpGet("GetVacantUnits")]
        [RequirePermission("leases:view")]
        public async Task<IActionResult> GetVacantUnits([FromQuery] int? includeUnitId) =>
            Ok(DataTableHelper.ToDictionaryList(await _service.GetVacantUnitsAsync(includeUnitId), true));

        public class ReverseLateFeeRequest { public int InvoiceId { get; set; } public string Reason { get; set; } = ""; }


        private static bool PaymentsValid(List<Payments>? payments)
        {
            if (payments == null || !payments.Any()) return false;
            foreach (var p in payments)
            {
                bool noCash = p.PaymentAmount <= 0 && (p.IsLateFeeWaived || p.DiscountAmount > 0);
                if (p.TenantId <= 0 || p.RentInvoiceId <= 0 || p.PaymentAmount < 0 || p.DiscountAmount < 0 ||
                    (p.PaymentAmount <= 0 && !noCash) ||
                    (!noCash && (p.PaymentDate == default || string.IsNullOrWhiteSpace(p.PaymentMethod))))
                    return false;
            }
            return true;
        }

        [HttpPost("SubmitPayments")]
        [RequirePermission("rentCollection:edit")]
        public async Task<IActionResult> SubmitPayments(List<Payments> payments, [FromServices] UserAccess access)
        {
            if (!PaymentsValid(payments))
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Invalid payment data" });
            // A discount or a late-fee waiver inside a payment needs its own permission
            if (payments.Any(p => p.DiscountAmount > 0 || p.DiscountPercent > 0) && !await access.CanAsync(Permissions.Discount))
                return StatusCode(StatusCodes.Status403Forbidden, new ApiResponse { IsSuccess = false, ErrorMessage = "You don't have permission to give a discount. Ask your administrator." });
            if (payments.Any(p => p.IsLateFeeWaived) && !await access.CanAsync(Permissions.LateFee))
                return StatusCode(StatusCodes.Status403Forbidden, new ApiResponse { IsSuccess = false, ErrorMessage = "You don't have permission to waive a late fee. Ask your administrator." });
            var response = await _service.CreateRentAsync(payments, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        // Payments are never edited: a wrong one is reversed as a whole and entered again
        [HttpPost("ReversePayment")]
        [RequirePermission("rentHistory:edit")]
        [RequirePermission("action:reversePayment")]
        public async Task<IActionResult> ReversePayment([FromBody] ReversePaymentRequest req)
        {
            if (req == null)
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Invalid input" });
            var response = await _service.ReversePaymentAsync(req.PaymentId, req.Reason, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        public class ReversePaymentRequest { public int PaymentId { get; set; } public string? Reason { get; set; } }

        // Adjustments (unlinked negative payments) are no longer created: a wrong payment is reversed instead
        // (ReversePayment). Existing adjustment rows stay and are shown as before.

        [HttpGet("GetRentCollection")]
        [RequirePermission("rentCollection:view")]
        public async Task<IActionResult> GetRentCollection()
        {
            var data = await _service.GetRentCollectionAsync();
            var list = DataTableHelper.ToDictionaryList(data, true);
            return Ok(list);
        }

        [HttpGet("GetTenantsWithRent")]
        [RequirePermission("rentCollection:view")]
        public async Task<IActionResult> GetTenantsWithRent()
        {
            var data = await _service.GetTenantsWithRentAsync();
            return Ok(DataTableHelper.ToDictionaryList(data, true));
        }

        [HttpGet("GetPaymentHistory")]
        [RequirePermission("rentCollection:view", "rentHistory:view")]
        public async Task<IActionResult> GetPaymentHistory([FromQuery] int invoiceId)
        {
            if (invoiceId <= 0)
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Invoice is required." });

            var data = await _service.GetPaymentHistoryAsync(invoiceId);
            var list = DataTableHelper.ToDictionaryList(data, true);
            return Ok(list);
        }

        // Bulk Operations
        [HttpPut("BulkUpdateDueDate")]
        [RequirePermission("rentCollection:edit")]
        public async Task<IActionResult> BulkUpdateDueDate([FromBody] BulkDueDateUpdateRequest request)
        {
            if (request == null)
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Invalid input" });
            var response = await _service.BulkUpdateDueDateAsync(request.InvoiceIds, request.NewDueDate, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        // For populating the tenant multiselect in Generate Invoices / Add Extra Charge
        [HttpGet("GetActiveTenants")]
        [RequirePermission("rentCollection:view", "leases:view")]
        public async Task<IActionResult> GetActiveTenants()
        {
            var data = await _service.GetActiveTenantsAsync();
            var list = DataTableHelper.ToDictionaryList(data, true);
            return Ok(list);
        }

        // CHANGED from query-param GET-style to a body, since it now carries an
        // optional tenant list (All / one / several) alongside month/year.
        [HttpPost("GenerateInvoices")]
        [RequirePermission("rentCollection:edit")]
        public async Task<IActionResult> GenerateInvoices([FromBody] GenerateInvoicesRequest request)
        {
            var response = await _service.GenerateInvoicesAsync(
                request.Month, request.Year, request.DueInDays, request.TenantIds, User.GetUserId());

            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        // One-time extra charges (Maintenance, Utility, Damage, Rent Correction, Other) for one or more tenants:
        // a separate invoice each, independent of monthly rent. All or nothing.
        [HttpPost("CreateExtraCharge")]
        [RequirePermission("rentCollection:edit")]
        public async Task<IActionResult> CreateExtraCharge([FromBody] ExtraChargeRequest request)
        {
            var response = await _service.CreateExtraChargeAsync(request, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }


        //Newly Added
        [HttpPost("ChargeLateFee")]
        [RequirePermission("rentCollection:edit")]
        [RequirePermission("action:lateFee")]
        public async Task<IActionResult> ChargeLateFee([FromQuery] int invoiceId)
        {
            if (invoiceId <= 0)
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Invoice id is required." });
            var response = await _service.ChargeLateFeeAsync(invoiceId);
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }
    }

    // Helper DTOs
    public class BulkDueDateUpdateRequest
    {
        public List<int> InvoiceIds { get; set; } = new();
        public DateTime NewDueDate { get; set; }
    }

    public class GenerateInvoicesRequest
    {
        public List<int>? TenantIds { get; set; } // null/empty = all active tenants
        public int Month { get; set; }
        public int Year { get; set; }
        public int? DueInDays { get; set; } // null = the Payment Due Days setting
    }
}
