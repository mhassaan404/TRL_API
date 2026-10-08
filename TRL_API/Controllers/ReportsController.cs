using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TRL_API.BLL;
using TRL_API.Helpers;
using TRL_API.Models;

namespace TRL_API.Controllers
{
    // Reports: read only.
    [Authorize(Roles = "Admin")]
    [Route("api/[controller]")]
    [ApiController]
    public class ReportsController : ControllerBase
    {
        private readonly IReportService _service;
        public ReportsController(IReportService service) => _service = service;

        [HttpGet("ArrearsAgeing")]
        public async Task<IActionResult> ArrearsAgeing() =>
            Ok(DataTableHelper.ToDictionaryList(await _service.GetArrearsAgeingAsync(), true));

        // Payments received from..to (payment dates, both days included)
        [HttpGet("Collections")]
        public async Task<IActionResult> Collections([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            var error = _service.ValidateCollectionsRange(from, to);
            if (error != null)
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = error });

            return Ok(DataTableHelper.ToDictionaryList(await _service.GetCollectionsAsync(from!.Value, to!.Value), true));
        }

        // Tenants for the statement picker (deleted tenants included, flagged)
        [HttpGet("StatementTenants")]
        public async Task<IActionResult> StatementTenants() =>
            Ok(DataTableHelper.ToDictionaryList(await _service.GetStatementTenantsAsync(), true));

        // Account statement of one tenant from..to: opening balance, charges, credits, running and closing balance
        [HttpGet("TenantStatement")]
        public async Task<IActionResult> TenantStatement([FromQuery] int tenantId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            if (tenantId <= 0)
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Please choose a tenant." });
            var error = _service.ValidateStatementRange(from, to);
            if (error != null)
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = error });

            var statement = await _service.GetTenantStatementAsync(tenantId, from!.Value, to!.Value);
            if (statement == null)
                return NotFound(new ApiResponse { IsSuccess = false, ErrorMessage = "Tenant not found." });
            return Ok(statement);
        }

        // Billed vs collected per month, fromMonth..toMonth (any day of the month), optionally for one building
        [HttpGet("BillingVsCollection")]
        public async Task<IActionResult> BillingVsCollection([FromQuery] DateTime? fromMonth, [FromQuery] DateTime? toMonth,
            [FromQuery] int? buildingId)
        {
            var error = _service.ValidateMonthRange(fromMonth, toMonth);
            if (error != null)
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = error });
            if (buildingId is <= 0)
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = "Invalid building." });

            return Ok(DataTableHelper.ToDictionaryList(
                await _service.GetBillingVsCollectionAsync(fromMonth!.Value, toMonth!.Value, buildingId), true));
        }

        // Buildings for report filters (removed buildings included, flagged)
        [HttpGet("Buildings")]
        public async Task<IActionResult> Buildings() =>
            Ok(DataTableHelper.ToDictionaryList(await _service.GetReportBuildingsAsync(), true));

        // Occupancy / rent roll: every unit in use with its status, current and next lease, rent and arrears (today)
        [HttpGet("RentRoll")]
        public async Task<IActionResult> RentRoll() =>
            Ok(DataTableHelper.ToDictionaryList(await _service.GetRentRollAsync(), true));

        // Maintenance jobs from..to (completed date, else reported date) with cost, billed and recovered amounts
        [HttpGet("MaintenanceCost")]
        public async Task<IActionResult> MaintenanceCost([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            var error = _service.ValidateMaintenanceRange(from, to);
            if (error != null)
                return BadRequest(new ApiResponse { IsSuccess = false, ErrorMessage = error });

            return Ok(DataTableHelper.ToDictionaryList(await _service.GetMaintenanceCostAsync(from!.Value, to!.Value), true));
        }
    }
}
