//using Azure;
//using Microsoft.AspNetCore.Authorization;
//using Microsoft.AspNetCore.Mvc;
//using System.Net;
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
//            return Ok(DataTableHelper.ToDictionaryList(data, true));
//        }

//        [HttpGet("GetBuildings")]
//        public async Task<IActionResult> GetBuildings()
//        {
//            var data = await _service.GetBuildings();
//            return Ok(DataTableHelper.ToDictionaryList(data, true));
//        }

//        [HttpGet("GetFloorsByBuilding/{buildingId}")]
//        public async Task<IActionResult> GetFloorsByBuilding(int buildingId)
//        {
//            var data = await _service.GetFloorsByBuilding(buildingId);
//            return Ok(DataTableHelper.ToDictionaryList(data, true));
//        }

//        [HttpGet("GetUnitsByFloor/{floorId}")]
//        public async Task<IActionResult> GetUnitsByFloor(int floorId)
//        {
//            var data = await _service.GetUnitsByFloor(floorId);
//            return Ok(DataTableHelper.ToDictionaryList(data, true));
//        }

//        [HttpGet("GetUnitsStatus")]
//        public async Task<IActionResult> GetUnitsStatus()
//        {
//            var data = await _service.GetUnitsStatus();
//            return Ok(DataTableHelper.ToDictionaryList(data, true));
//        }

//        [HttpPost("SaveBuilding")]
//        public async Task<IActionResult> SaveBuilding([FromBody] UpdatePropertyRequest req)
//        {
//            var response = await _service.SaveBuilding(req.buildingName, req.cityId, req.typeId, req.address);

//            return response.IsSuccess ? Ok(response) : BadRequest(response);
//        }

//        [HttpPost("SaveFloor")]
//        public async Task<IActionResult> SaveFloor([FromBody] UpdatePropertyRequest req)
//        {
//            var response = await _service.SaveFloor(req.buildingId, req.floorNumber);

//            return response.IsSuccess ? Ok(response) : BadRequest(response);
//        }

//        [HttpPost("SaveUnit")]
//        public async Task<IActionResult> SaveUnit([FromBody] UpdatePropertyRequest req)
//        {
//            var response = await _service.SaveUnit(req.floorId, req.buildingId,
//                req.unitNumber, req.statusId, req.baseRent, req.note);

//            return response.IsSuccess ? Ok(response) : BadRequest(response);
//        }

//        [HttpPut("UpdateUnit")]
//        public async Task<IActionResult> UpdateUnit([FromBody] UpdatePropertyRequest req)
//        {
//            var response = await _service.UpdateUnit(req.unitId, req.floorId, req.buildingId,
//                req.unitNumber, req.statusId, req.baseRent, req.note);

//            return response.IsSuccess ? Ok(response) : BadRequest(response);
//        }

//        [HttpDelete("DeleteUnit/{unitId}")]
//        public async Task<IActionResult> DeleteUnit(int unitId)
//        {
//            var response = await _service.DeleteUnitAsync(unitId);

//            return response.IsSuccess ? Ok(response) : BadRequest(response);
//        }

//        public class UpdatePropertyRequest
//        {
//            public int unitId { get; set; }
//            public int floorId { get; set; }
//            public int buildingId { get; set; }
//            public string? buildingName { get; set; }
//            public int cityId { get; set; }
//            public int typeId { get; set; }
//            public int floorNumber { get; set; }
//            public int unitNumber { get; set; }
//            public int statusId { get; set; }
//            public double baseRent { get; set; }
//            public string? note { get; set; }
//            public string? address { get; set; }
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

        [HttpGet("GetUnitsByFloor/{floorId}")]
        public async Task<IActionResult> GetUnitsByFloor(int floorId)
        {
            var data = await _service.GetUnitsByFloor(floorId);
            return Ok(DataTableHelper.ToDictionaryList(data, true));
        }

        [HttpGet("GetUnitsStatus")]
        public async Task<IActionResult> GetUnitsStatus()
        {
            var data = await _service.GetUnitsStatus();
            return Ok(DataTableHelper.ToDictionaryList(data, true));
        }

        [HttpGet("GetCities")]
        public async Task<IActionResult> GetCities()
        {
            var data = await _service.GetCities();
            return Ok(DataTableHelper.ToDictionaryList(data, true));
        }

        [HttpGet("GetBuildingTypes")]
        public async Task<IActionResult> GetBuildingTypes()
        {
            var data = await _service.GetBuildingTypes();
            return Ok(DataTableHelper.ToDictionaryList(data, true));
        }

        // ---------------- BUILDING ----------------

        [HttpPost("SaveBuilding")]
        public async Task<IActionResult> SaveBuilding([FromBody] UpdatePropertyRequest req)
        {
            var response = await _service.SaveBuilding(req.buildingName, req.cityId, req.typeId, req.address);
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpPut("UpdateBuilding/{id}")]
        public async Task<IActionResult> UpdateBuilding(int id, [FromBody] UpdatePropertyRequest req)
        {
            var response = await _service.UpdateBuilding(id, req.buildingName, req.cityId, req.typeId, req.address);
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpDelete("DeleteBuilding/{id}")]
        public async Task<IActionResult> DeleteBuilding(int id)
        {
            var response = await _service.DeleteBuilding(id);
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        // ---------------- FLOOR ----------------

        [HttpPost("SaveFloor")]
        public async Task<IActionResult> SaveFloor([FromBody] UpdatePropertyRequest req)
        {
            var response = await _service.SaveFloor(req.buildingId, req.floorNumber);
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpPut("UpdateFloor/{id}")]
        public async Task<IActionResult> UpdateFloor(int id, [FromBody] UpdatePropertyRequest req)
        {
            var response = await _service.UpdateFloor(id, req.floorNumber);
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpDelete("DeleteFloor/{id}")]
        public async Task<IActionResult> DeleteFloor(int id)
        {
            var response = await _service.DeleteFloor(id);
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        // ---------------- UNIT ----------------

        [HttpPost("SaveUnit")]
        public async Task<IActionResult> SaveUnit([FromBody] UpdatePropertyRequest req)
        {
            var response = await _service.SaveUnit(req.floorId, req.buildingId,
                req.unitNumber, req.statusId, req.baseRent, req.note, req.propertyType);

            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpPut("UpdateUnit")]
        public async Task<IActionResult> UpdateUnit([FromBody] UpdatePropertyRequest req)
        {
            var response = await _service.UpdateUnit(req.unitId, req.floorId, req.buildingId,
                req.unitNumber, req.statusId, req.baseRent, req.note, req.propertyType);

            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpDelete("DeleteUnit/{unitId}")]
        public async Task<IActionResult> DeleteUnit(int unitId)
        {
            var response = await _service.DeleteUnitAsync(unitId);
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        public class UpdatePropertyRequest
        {
            public int unitId { get; set; }
            public int floorId { get; set; }
            public int buildingId { get; set; }
            public string? buildingName { get; set; }
            public int cityId { get; set; }
            public int typeId { get; set; }
            public int floorNumber { get; set; }
            public int unitNumber { get; set; }
            public int statusId { get; set; }
            public double baseRent { get; set; }
            public string? note { get; set; }
            public string? address { get; set; }
            public string? propertyType { get; set; }
        }
    }
}