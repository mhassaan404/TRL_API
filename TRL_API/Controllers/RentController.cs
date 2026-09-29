using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using TRL_API.BLL;
using TRL_API.Helpers;
using TRL_API.Models;

namespace TRL_API.Controllers
{
    [Authorize(Roles = "Admin,Tenant")]
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
        public async Task<IActionResult> GetTenants()
        {
            var data = await _service.GetTenantsAsync();
            var list = DataTableHelper.ToDictionaryList(data);
            return Ok(list);
        }

        // Invoices
        [HttpGet("GetInvoicesByTenant")]
        public async Task<IActionResult> GetInvoicesByTenant([FromQuery] int tenantId)
        {
            if (tenantId <= 0)
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Tenant is required." });

            var data = await _service.GetInvoicesByTenantAsync(tenantId);
            var list = DataTableHelper.ToDictionaryList(data);
            return Ok(list);
        }

        [HttpGet("GetPaymentHistoryById")]
        public async Task<IActionResult> GetPaymentHistoryById([FromQuery] int invoiceId)
        {
            if (invoiceId <= 0)
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Invoice is required." });
            var data = await _service.GetPaymentHistoryByIdAsync(invoiceId);
            var list = DataTableHelper.ToDictionaryList(data, true);
            return Ok(list);
        }

        [HttpGet("GetUnpaidInvoiceByTenant")]
        public async Task<IActionResult> GetUnpaidInvoiceByTenant([FromQuery] int tenantId)
        {
            if (tenantId <= 0)
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Tenant is required." });
            var data = await _service.GetUnpaidInvoicesByTenantAsync(tenantId);
            return Ok(data);
        }


        [Authorize(Roles = "Admin")]
        [HttpPost("ReverseLateFee")]
        public async Task<IActionResult> ReverseLateFee([FromBody] ReverseLateFeeRequest req)
        {
            var response = await _service.ReverseLateFeeAsync(req.InvoiceId, req.Reason, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpGet("GetOccupancy")]
        public async Task<IActionResult> GetOccupancy() =>
            Ok(DataTableHelper.ToDictionaryList(await _service.GetOccupancyAsync(), true));

        [HttpGet("GetVacantUnits")]
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

        [Authorize(Roles = "Admin")]
        [HttpPost("SubmitPayments")]
        public async Task<IActionResult> SubmitPayments(List<Payments> payments)
        {
            if (!PaymentsValid(payments))
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Invalid payment data" });
            var response = await _service.CreateRentAsync(payments, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [Authorize(Roles = "Admin")]
        [HttpPut("UpdatePayments")]
        public async Task<IActionResult> UpdatePayments(List<Payments> payments)
        {
            if (!PaymentsValid(payments))
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Invalid payment data" });
            var response = await _service.UpdatePaymentsAsync(payments, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }


        //// NEW — was missing. Same validation/flow as SubmitPayments; the frontend
        //// calls this from the "Update" button on an existing invoice row.
        [Authorize(Roles = "Admin")]
        [HttpDelete("DeletePayment")]
        public async Task<IActionResult> DeletePayment([FromQuery] int paymentId)
        {
            if (paymentId <= 0)
                return Ok(new ApiResponse { IsSuccess = false, ErrorMessage = "Invoice id is required." });

            var response = await _service.DeletePaymentAsync(paymentId);
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        // Payments
        [Authorize(Roles = "Admin")]
        [HttpPost("CreatePaymentAdjustment")]
        public async Task<IActionResult> CreatePaymentAdjustment(Payments payments)
        {
            if (payments == null || payments.RentInvoiceId == 0)
            {
                return Ok(new ApiResponse
                {
                    IsSuccess = false,
                    ErrorMessage = "Invalid input"
                });
            }

            int userId = User.GetUserId();
            var response = await _service.CreatePaymentAdjustmentAsync(payments, userId);
            return Ok(response);
        }

        [HttpGet("GetRentCollection")]
        public async Task<IActionResult> GetRentCollection()
        {
            var data = await _service.GetRentCollectionAsync();
            var list = DataTableHelper.ToDictionaryList(data, true);
            return Ok(list);
        }

        [HttpGet("GetTenantsWithRent")]
        public async Task<IActionResult> GetTenantsWithRent()
        {
            var data = await _service.GetTenantsWithRentAsync();
            return Ok(DataTableHelper.ToDictionaryList(data, true));
        }

        [HttpGet("GetPaymentHistory")]
        public async Task<IActionResult> GetPaymentHistory([FromQuery] int invoiceId)
        {
            if (invoiceId <= 0)
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Invoice is required." });

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
            return Ok(response);
        }

        // For populating the tenant multiselect in Generate Invoices / Add Extra Charge
        [HttpGet("GetActiveTenants")]
        public async Task<IActionResult> GetActiveTenants()
        {
            var data = await _service.GetActiveTenantsAsync();
            var list = DataTableHelper.ToDictionaryList(data, true);
            return Ok(list);
        }

        // CHANGED from query-param GET-style to a body, since it now carries an
        // optional tenant list (All / one / several) alongside month/year.
        [Authorize(Roles = "Admin")]
        [HttpPost("GenerateInvoices")]
        public async Task<IActionResult> GenerateInvoices([FromBody] GenerateInvoicesRequest request)
        {
            var response = await _service.GenerateInvoicesAsync(
                request.Month, request.Year, request.DueInDays, request.TenantIds);

            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        // NEW — one-time extra charges (Maintenance, Late Fine, Security Deposit,
        // etc.) for one, several, or all tenants, independent of monthly rent.
        [Authorize(Roles = "Admin")]
        [HttpPost("CreateExtraCharge")]
        public async Task<IActionResult> CreateExtraCharge([FromBody] ExtraChargeRequest request)
        {
            var response = await _service.CreateExtraChargeAsync(
                request.TenantIds, request.Month, request.Year,
                request.ChargeType, request.Description, request.Amount, request.DueInDays);

            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }


        //Newly Added
        [Authorize(Roles = "Admin")]
        [HttpPost("ChargeLateFee")]
        public async Task<IActionResult> ChargeLateFee([FromQuery] int invoiceId)
        {
            if (invoiceId <= 0)
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Invoice id is required." });
            var response = await _service.ChargeLateFeeAsync(invoiceId);
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpGet("GetAllPayments")]
        public async Task<IActionResult> GetAllPayments([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            var data = await _service.GetAllPaymentsAsync(from, to);
            return Ok(DataTableHelper.ToDictionaryList(data, true));
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
        public int DueInDays { get; set; } = 5;
    }

    public class ExtraChargeRequest
    {
        public List<int> TenantIds { get; set; } = new();
        public int Month { get; set; }
        public int Year { get; set; }
        public string ChargeType { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal Amount { get; set; }
        public int DueInDays { get; set; } = 5;
    }
}