//using Microsoft.AspNetCore.Authorization;
//using Microsoft.AspNetCore.Mvc;
//using System.Collections.Generic;
//using TRL_API.BLL;
//using TRL_API.Helpers;
//using TRL_API.Models;

//namespace TRL_API.Controllers
//{
//    //[Authorize(Roles = "Admin,Tenant")]
//    [Route("api/[controller]")]
//    [ApiController]
//    public class RentController : ControllerBase
//    {
//        private readonly RentService _service;

//        public RentController(RentService service)
//        {
//            _service = service;
//        }

//        // Tenants
//        [HttpGet("GetTenants")]
//        public async Task<IActionResult> GetTenants()
//        {
//            var data = await _service.GetTenantsAsync();
//            var list = DataTableHelper.ToDictionaryList(data);
//            return Ok(list);
//        }

//        [HttpGet("GetStatusList")]
//        public async Task<IActionResult> GetStatusList()
//        {
//            var data = await _service.GetStatusListAsync();
//            var list = DataTableHelper.ToDictionaryList(data);
//            return Ok(list);
//        }

//        // Invoices
//        [HttpGet("GetInvoicesByTenant")]
//        public async Task<IActionResult> GetInvoicesByTenant([FromQuery] int tenantId)
//        {
//            if (tenantId <= 0)
//                return Ok("TenantId is required and must be greater than zero.");

//            var data = await _service.GetInvoicesByTenantAsync(tenantId);
//            var list = DataTableHelper.ToDictionaryList(data);
//            return Ok(list);
//        }

//        // Invoices By Id
//        [HttpGet("GetInvoiceById")]
//        public async Task<IActionResult> GetInvoiceById([FromQuery] int invoiceId)
//        {
//            if (invoiceId <= 0)
//                return Ok("Invoice Id is required and must be greater than zero.");

//            try
//            {
//                var data = await _service.GetInvoiceByIdAsync(invoiceId);
//                return Ok(data); // DTO contains both Invoices and Summary
//            }
//            catch (Exception ex)
//            {
//                return StatusCode(500, $"Failed to load invoice: {ex.Message}");
//            }
//        }

//        [HttpGet("GetPaymentHistoryById")]
//        public async Task<IActionResult> GetPaymentHistoryById([FromQuery] int invoiceId)
//        {
//            if (invoiceId <= 0)
//                return Ok("Invoice Id is required and must be greater than zero.");

//            try
//            {
//                var data = await _service.GetPaymentHistoryByIdAsync(invoiceId);
//                var list = DataTableHelper.ToDictionaryList(data, true);
//                return Ok(list);
//            }
//            catch (Exception ex)
//            {
//                return StatusCode(500, $"Failed to load invoice: {ex.Message}");
//            }
//        }

//        [HttpGet("GetUnpaidInvoiceByTenant")]
//        public async Task<IActionResult> GetUnpaidInvoiceByTenant([FromQuery] int tenantId)
//        {
//            if (tenantId <= 0)
//                return Ok(new ApiResponse { IsSuccess = false, ErrorMessage = "Tenant is required." });

//            try
//            {
//                var data = await _service.GetUnpaidInvoicesByTenantAsync(tenantId);
//                return Ok(data);
//            }
//            catch (Exception ex)
//            {
//                return StatusCode(500, $"Failed to load invoice: {ex.Message}");
//            }
//        }

//        // Payments
//        [HttpPost("SubmitPayments")]
//        public async Task<IActionResult> SubmitPayments(List<Payments> payments)
//        {
//            if (!ModelState.IsValid || payments == null || !payments.Any())
//            {
//                return Ok(new ApiResponse
//                {
//                    IsSuccess = false,
//                    ErrorMessage = "Invalid input"
//                });
//            }

//            foreach (var p in payments)
//            {
//                if (p.TenantId <= 0 ||
//                    p.RentInvoiceId <= 0 ||
//                    p.PaymentAmount <= 0 ||
//                    p.PaymentDate == default ||
//                    string.IsNullOrWhiteSpace(p.PaymentMethod))
//                {
//                    return Ok(new ApiResponse
//                    {
//                        IsSuccess = false,
//                        ErrorMessage = "Invalid payment data"
//                    });
//                }
//            }

//            int userId = User.GetUserId();
//            var response = await _service.CreateRentAsync(payments, userId);
//            return response.IsSuccess ? Ok(response) : BadRequest(response);
//        }

//        // NEW — was missing. Same validation/flow as SubmitPayments; the frontend
//        // calls this from the "Update" button on an existing invoice row.
//        [HttpPut("UpdatePayments")]
//        public async Task<IActionResult> UpdatePayments(List<Payments> payments)
//        {
//            if (!ModelState.IsValid || payments == null || !payments.Any())
//            {
//                return Ok(new ApiResponse
//                {
//                    IsSuccess = false,
//                    ErrorMessage = "Invalid input"
//                });
//            }

//            foreach (var p in payments)
//            {
//                if (p.TenantId <= 0 ||
//                    p.RentInvoiceId <= 0 ||
//                    p.PaymentAmount <= 0 ||
//                    p.PaymentDate == default ||
//                    string.IsNullOrWhiteSpace(p.PaymentMethod))
//                {
//                    return Ok(new ApiResponse
//                    {
//                        IsSuccess = false,
//                        ErrorMessage = "Invalid payment data"
//                    });
//                }
//            }

//            int userId = User.GetUserId();
//            var response = await _service.UpdatePaymentsAsync(payments, userId);
//            return response.IsSuccess ? Ok(response) : BadRequest(response);
//        }

//        // NEW — was missing. The frontend's row-level "Delete" button calls this
//        // with the invoice's id (as query param "paymentId", for historical
//        // naming reasons on the frontend). Deletes the most recent payment on
//        // that invoice and recalculates the invoice's balance/status.

//        //[HttpDelete("DeletePayment")]
//        //public async Task<IActionResult> DeletePayment([FromQuery] int paymentId)
//        //{
//        //    if (paymentId <= 0)
//        //        return Ok(new ApiResponse { IsSuccess = false, ErrorMessage = "Invoice id is required." });

//        //    var response = await _service.DeletePaymentAsync(paymentId);
//        //    return response.IsSuccess ? Ok(response) : BadRequest(response);
//        //}

//        // Payments
//        [HttpPost("CreatePaymentAdjustment")]
//        public async Task<IActionResult> CreatePaymentAdjustment(Payments payments)
//        {
//            if (payments == null || payments.RentInvoiceId == 0)
//            {
//                return Ok(new ApiResponse
//                {
//                    IsSuccess = false,
//                    ErrorMessage = "Invalid input"
//                });
//            }

//            //int userId = User.GetUserId();
//            int userId = 1;
//            var response = await _service.CreatePaymentAdjustmentAsync(payments, userId);
//            return Ok(response);
//        }

//        [HttpGet("GetRentCollection")]
//        public async Task<IActionResult> GetRentCollection()
//        {
//            var data = await _service.GetRentCollectionAsync();
//            var list = DataTableHelper.ToDictionaryList(data, true);
//            return Ok(list);
//        }

//        [HttpGet("GetPaymentHistory")]
//        public async Task<IActionResult> GetPaymentHistory([FromQuery] int invoiceId)
//        {
//            if (invoiceId <= 0)
//                return Ok(new ApiResponse { IsSuccess = false, ErrorMessage = "InvoiceId is required and must be greater than zero." });

//            var data = await _service.GetPaymentHistoryAsync(invoiceId);
//            var list = DataTableHelper.ToDictionaryList(data);
//            return Ok(list);
//        }

//        // Bulk Operations
//        [Authorize(Roles = "Admin")]
//        [HttpPut("BulkUpdateDueDate")]
//        public async Task<IActionResult> BulkUpdateDueDate([FromBody] BulkDueDateUpdateRequest request)
//        {
//            if (request.InvoiceIds == null || request.InvoiceIds.Count == 0)
//                return Ok(new ApiResponse { IsSuccess = false, ErrorMessage = "Invoice list cannot be empty." });

//            var response = await _service.BulkUpdateDueDateAsync(request.InvoiceIds, request.NewDueDate);
//            return Ok(response);
//        }

//        // NEW — bulk invoice generation. Manual trigger, no scheduled service
//        // required: pick a month, click, done. Skips any tenant who already has
//        // an invoice for that month so it's always safe to click again.
//        [HttpPost("GenerateInvoices")]
//        public async Task<IActionResult> GenerateInvoices([FromQuery] int? month, [FromQuery] int? year, [FromQuery] int dueInDays = 5)
//        {
//            var targetMonth = month ?? DateTime.Today.Month;
//            var targetYear = year ?? DateTime.Today.Year;

//            var response = await _service.GenerateInvoicesAsync(targetMonth, targetYear, dueInDays);
//            return response.IsSuccess ? Ok(response) : BadRequest(response);
//        }
//    }

//    // Helper DTO
//    public class BulkDueDateUpdateRequest
//    {
//        public List<int> InvoiceIds { get; set; } = new();
//        public DateTime NewDueDate { get; set; }
//    }
//}



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
                return StatusCode(500, $"Failed to load invoice: {ex.Message}");
            }
        }


        [HttpPost("ReverseLateFee")]
        public async Task<IActionResult> ReverseLateFee([FromBody] ReverseLateFeeRequest req)
        {
            var response = await _service.ReverseLateFeeAsync(req.InvoiceId, req.Reason, 1);
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

        [HttpPost("SubmitPayments")]
        public async Task<IActionResult> SubmitPayments(List<Payments> payments)
        {
            if (!PaymentsValid(payments))
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Invalid payment data" });
            var response = await _service.CreateRentAsync(payments, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpPut("UpdatePayments")]
        public async Task<IActionResult> UpdatePayments(List<Payments> payments)
        {
            if (!PaymentsValid(payments))
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Invalid payment data" });
            var response = await _service.UpdatePaymentsAsync(payments, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }



        // Payments
        //[HttpPost("SubmitPayments")]
        //public async Task<IActionResult> SubmitPayments(List<Payments> payments)
        //{
        //    if (!ModelState.IsValid || payments == null || !payments.Any())
        //    {
        //        return Ok(new ApiResponse
        //        {
        //            IsSuccess = false,
        //            ErrorMessage = "Invalid input"
        //        });
        //    }

        //    foreach (var p in payments)
        //    {
        //        bool isNoCashAction = p.PaymentAmount <= 0 && (p.IsLateFeeWaived || p.DiscountAmount > 0);

        //        if (p.TenantId <= 0 ||
        //            p.RentInvoiceId <= 0 ||
        //            (p.PaymentAmount <= 0 && !isNoCashAction) ||
        //            p.PaymentAmount < 0 ||
        //            (!isNoCashAction && p.PaymentDate == default) ||
        //            (!isNoCashAction && string.IsNullOrWhiteSpace(p.PaymentMethod)))
        //        {
        //            return Ok(new ApiResponse
        //            {
        //                IsSuccess = false,
        //                ErrorMessage = "Invalid payment data"
        //            });
        //        }
        //    }

        //    int userId = User.GetUserId();
        //    var response = await _service.CreateRentAsync(payments, userId);
        //    return response.IsSuccess ? Ok(response) : BadRequest(response);
        //}

        //// NEW — was missing. Same validation/flow as SubmitPayments; the frontend
        //// calls this from the "Update" button on an existing invoice row.
        //[HttpPut("UpdatePayments")]
        //public async Task<IActionResult> UpdatePayments(List<Payments> payments)
        //{
        //    if (!ModelState.IsValid || payments == null || !payments.Any())
        //    {
        //        return Ok(new ApiResponse
        //        {
        //            IsSuccess = false,
        //            ErrorMessage = "Invalid input"
        //        });
        //    }

        //    foreach (var p in payments)
        //    {
        //        if (p.TenantId <= 0 ||
        //            p.RentInvoiceId <= 0 ||
        //            p.PaymentAmount <= 0 ||
        //            p.PaymentDate == default ||
        //            string.IsNullOrWhiteSpace(p.PaymentMethod))
        //        {
        //            return Ok(new ApiResponse
        //            {
        //                IsSuccess = false,
        //                ErrorMessage = "Invalid payment data"
        //            });
        //        }
        //    }

        //    int userId = User.GetUserId();
        //    var response = await _service.UpdatePaymentsAsync(payments, userId);
        //    return response.IsSuccess ? Ok(response) : BadRequest(response);
        //}

        // NEW — was missing. The frontend's row-level "Delete" button calls this
        // with the invoice's id (as query param "paymentId", for historical
        // naming reasons on the frontend). Deletes the most recent payment on
        // that invoice and recalculates the invoice's balance/status.
        [HttpDelete("DeletePayment")]
        public async Task<IActionResult> DeletePayment([FromQuery] int paymentId)
        {
            if (paymentId <= 0)
                return Ok(new ApiResponse { IsSuccess = false, ErrorMessage = "Invoice id is required." });

            var response = await _service.DeletePaymentAsync(paymentId);
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

            //int userId = User.GetUserId();
            int userId = 1;
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

        [HttpPut("UpdateTenantMonthlyRent")]
        public async Task<IActionResult> UpdateTenantMonthlyRent([FromBody] UpdateTenantRentRequest request)
        {
            var response = await _service.UpdateTenantMonthlyRentAsync(request.TenantId, request.MonthlyRent);
            return response.IsSuccess ? Ok(response) : BadRequest(response);
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
        [HttpPost("GenerateInvoices")]
        public async Task<IActionResult> GenerateInvoices([FromBody] GenerateInvoicesRequest request)
        {
            var response = await _service.GenerateInvoicesAsync(
                request.Month, request.Year, request.DueInDays, request.TenantIds);

            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        // NEW — one-time extra charges (Maintenance, Late Fine, Security Deposit,
        // etc.) for one, several, or all tenants, independent of monthly rent.
        [HttpPost("CreateExtraCharge")]
        public async Task<IActionResult> CreateExtraCharge([FromBody] ExtraChargeRequest request)
        {
            var response = await _service.CreateExtraChargeAsync(
                request.TenantIds, request.Month, request.Year,
                request.ChargeType, request.Description, request.Amount, request.DueInDays);

            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }


        //Newly Added
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
    public class UpdateTenantRentRequest
    {
        public int TenantId { get; set; }
        public decimal MonthlyRent { get; set; }
    }

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