using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TRL_API.BLL;
using TRL_API.Helpers;

namespace TRL_API.Controllers
{
    [Authorize(Roles = "Admin,Tenant")]
    [Route("api/[controller]")]
    [ApiController]
    public class PropertiesController : ControllerBase
    {
        private readonly PropertiesService _service;
        public PropertiesController(PropertiesService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetProperties()
        {
            var data = await _service.GetProperties();
            var list = DataTableHelper.ToDictionaryList(data);
            return Ok(list);
        }
    }
}
