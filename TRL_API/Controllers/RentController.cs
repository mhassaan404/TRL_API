using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
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

        // Invoices By Id
        [HttpGet("GetInvoiceById")]
        public async Task<IActionResult> GetInvoiceById([FromQuery] int invoiceId)
        {
            if (invoiceId <= 0)
                return Ok("Invoice Id is required and must be greater than zero.");

            try
            {
                var data = await _service.GetInvoiceByIdAsync(invoiceId);
                return Ok(data); // DTO contains both Invoices and Summary
            }
            catch (Exception ex)
            {
                // log exception if needed
                return StatusCode(500, $"Failed to load invoice: {ex.Message}");
            }
        }

        [HttpGet("GetPaymentHistoryById")]
        public async Task<IActionResult> GetPaymentHistoryById([FromQuery] int invoiceId)
        {
            if (invoiceId <= 0)
                return Ok("Invoice Id is required and must be greater than zero.");

            try
            {
                var data = await _service.GetPaymentHistoryByIdAsync(invoiceId);
                var list = DataTableHelper.ToDictionaryList(data, true);
                return Ok(list);
            }
            catch (Exception ex)
            {
                // log exception if needed
                return StatusCode(500, $"Failed to load invoice: {ex.Message}");
            }
        }

        [HttpGet("GetUnpaidInvoiceByTenant")]
        public async Task<IActionResult> GetUnpaidInvoiceByTenant([FromQuery] int tenantId)
        {
            if (tenantId <= 0)
                return Ok(new ApiResponse { IsSuccess = false, ErrorMessage = "Tenant is required." });

            try
            {
                var data = await _service.GetUnpaidInvoicesByTenantAsync(tenantId);
                return Ok(data);
            }
            catch (Exception ex)
            {
                // log exception if needed
                return StatusCode(500, $"Failed to load invoice: {ex.Message}");
            }
        }

        // Payments
        //[HttpPost("SubmitPayments")]
        //public async Task<IActionResult> SubmitPayments(List<Payments> payments)
        //{
        //    if ((!ModelState.IsValid) || (payments.TenantId <= 0 || payments.RentInvoiceId <= 0 || payments.PaymentAmount <= 0))
        //        return Ok(new ApiResponse { IsSuccess = false, ErrorMessage = "Invalid input parameters." });

        //    Payments payments2 = new Payments();    
        //    var response = await _service.CreateRentAsync(payments2);
        //    if (response.IsSuccess)
        //        return Ok(response);

        //    return Ok(response);
        //}

        // Payments
        [HttpPost("SubmitPayments")]
        public async Task<IActionResult> SubmitPayments(List<Payments> payments)
        {
            if (!ModelState.IsValid || payments == null || !payments.Any())
            {
                return Ok(new ApiResponse
                {
                    IsSuccess = false,
                    ErrorMessage = "Invalid input"
                });
            }

            foreach (var p in payments)
            {
                if (p.TenantId <= 0 ||
                    p.RentInvoiceId <= 0 ||
                    p.PaymentAmount <= 0 ||
                    p.PaymentDate == default ||
                    string.IsNullOrWhiteSpace(p.PaymentMethod))
                {
                    return Ok(new ApiResponse
                    {
                        IsSuccess = false,
                        ErrorMessage = "Invalid payment data"
                    });
                }
            }

            int userId = User.GetUserId();
            var response = await _service.CreateRentAsync(payments, userId);
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        // Payments
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
            var list = await _service.GetRentCollectionAsync();
            //var list = DataTableHelper.ToDictionaryList(data, true);
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
