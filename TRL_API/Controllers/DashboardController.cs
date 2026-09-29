using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TRL_API.BLL;
using TRL_API.Data;
using TRL_API.Helpers;
using TRL_API.Models;

namespace TRL_API.Controllers
{
    [Authorize(Roles = "Admin,Tenant")]
    [Route("api/[controller]")]
    [ApiController]
    public class DashboardController : ControllerBase
    {
        private readonly IDashboardService _service;
        public DashboardController(IDashboardService service)
        {
            _service = service;
        }

        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboardData()
        {
            var data = await _service.GetDashboardData();
            var list = DataTableHelper.ToDictionaryList(data);
            return Ok(list);
        }
    }
}
