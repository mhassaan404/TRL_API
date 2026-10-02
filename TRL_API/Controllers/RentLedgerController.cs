using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TRL_API.Controllers
{
    [Authorize(Roles = "Admin")]
    [ApiController]
    [Route("api/[controller]")]
    public class RentLedgerController : Controller
    {
        [HttpGet("GetLedger")]
        [Authorize(Roles = "Admin")]
        public IActionResult GetLedger()
        {
            // Only authorized users can access
            return Ok(new { Message = "You are authorized!" });
        }

        [HttpPost("AddPayment")]
        [Authorize(Roles = "Admin")]
        public IActionResult AddPayment()
        {
            // Only Admin can add payments
            return Ok(new { Message = "Payment added!" });
        }
    }
}
