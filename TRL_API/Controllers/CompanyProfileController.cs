using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TRL_API.BLL;
using TRL_API.Helpers;
using TRL_API.Models;

namespace TRL_API.Controllers
{
    // The client's own details for printed documents (Administration > Company Profile)
    [Authorize(Roles = "Admin")]
    [Route("api/[controller]")]
    [ApiController]
    public class CompanyProfileController : ControllerBase
    {
        private readonly ICompanyProfileService _service;
        public CompanyProfileController(ICompanyProfileService service) => _service = service;

        [HttpGet("Get")]
        public async Task<IActionResult> Get() => Ok(await _service.GetAsync());

        // The logo travels as base64 in the JSON body (at most 200 KB, checked in the service)
        [HttpPost("Save")]
        [RequestSizeLimit(1_000_000)]
        public async Task<IActionResult> Save(SaveCompanyProfileRequest req)
        {
            var response = await _service.SaveAsync(req, User.GetUserId());
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }
    }
}
