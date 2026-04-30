//using Microsoft.AspNetCore.Authorization;
//using Microsoft.AspNetCore.Http;
//using Microsoft.AspNetCore.Mvc;
//using TRL_API.BLL;
//using TRL_API.Helpers;

//namespace TRL_API.Controllers
//{
//    [Authorize(Roles = "Admin,Tenant")]
//    [Route("api/[controller]")]
//    [ApiController]
//    public class PropertiesController : ControllerBase
//    {
//        private readonly PropertiesService _service;
//        public PropertiesController(PropertiesService service)
//        {
//            _service = service;
//        }

//        [HttpGet("GetProperties")]
//        public async Task<IActionResult> GetProperties()
//        {
//            var data = await _service.GetProperties();
//            var list = DataTableHelper.ToDictionaryList(data, true);
//            return Ok(list);
//        }

//        [HttpGet("GetBuildings")]
//        public async Task<IActionResult> GetBuildings()
//        {
//            var data = await _service.GetBuildings();
//            var list = DataTableHelper.ToDictionaryList(data, true);
//            return Ok(list);
//        }
//    }
//}




using Microsoft.AspNetCore.Authorization;
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

        [HttpGet("GetProperties")]
        public async Task<IActionResult> GetProperties()
        {
            var data = await _service.GetProperties();
            return Ok(DataTableHelper.ToDictionaryList(data, true));
        }

        [HttpGet("GetBuildings")]
        public async Task<IActionResult> GetBuildings()
        {
            var data = await _service.GetBuildings();
            return Ok(DataTableHelper.ToDictionaryList(data, true));
        }

        [HttpGet("GetFloorsByBuilding/{buildingId}")]
        public async Task<IActionResult> GetFloorsByBuilding(int buildingId)
        {
            var data = await _service.GetFloorsByBuilding(buildingId);
            return Ok(DataTableHelper.ToDictionaryList(data, true));
        }

        [HttpPost("SaveBuilding")]
        public async Task<IActionResult> SaveBuilding([FromBody] dynamic req)
        {
            var data = await _service.SaveBuilding(
                (string)req.buildingName,
                (int)req.cityId,
                (int)req.typeId,
                (string)req.address
            );

            return Ok(DataTableHelper.ToDictionaryList(data, true));
        }

        [HttpPost("SaveFloor")]
        public async Task<IActionResult> SaveFloor([FromBody] dynamic req)
        {
            var data = await _service.SaveFloor(
                (int)req.buildingId,
                (int)req.floorNumber
            );

            return Ok(DataTableHelper.ToDictionaryList(data, true));
        }

        [HttpPost("SaveUnit")]
        public async Task<IActionResult> SaveUnit([FromBody] dynamic req)
        {
            var data = await _service.SaveUnit(
                (int)req.floorId,
                (int)req.buildingId,
                (int)req.unitNumber,
                (int)req.statusId,
                (double)req.baseRent,
                (string)req.note
            );

            return Ok(DataTableHelper.ToDictionaryList(data, true));
        }

        [HttpPut("UpdateUnit/{unitId}")]
        public async Task<IActionResult> UpdateUnit(int unitId, [FromBody] dynamic req)
        {
            var data = await _service.UpdateUnit(
                unitId,
                (int)req.floorId,
                (int)req.buildingId,
                (int)req.unitNumber,
                (int)req.statusId,
                (double)req.baseRent,
                (string)req.note
            );

            return Ok(DataTableHelper.ToDictionaryList(data, true));
        }

        [HttpDelete("DeleteUnit/{unitId}")]
        public async Task<IActionResult> DeleteUnit(int unitId)
        {
            var data = await _service.DeleteUnit(unitId);
            return Ok(DataTableHelper.ToDictionaryList(data, true));
        }
    }
}