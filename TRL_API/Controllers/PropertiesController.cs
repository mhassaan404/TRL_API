using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TRL_API.BLL;
using TRL_API.Helpers;

namespace TRL_API.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class PropertiesController : ControllerBase
    {
        private readonly IPropertiesService _service;

        public PropertiesController(IPropertiesService service)
        {
            _service = service;
        }

        [HttpGet("GetProperties")]
        [RequirePermission("properties:view", "occupancy:view")]
        public async Task<IActionResult> GetProperties()
        {
            var data = await _service.GetProperties();
            return Ok(DataTableHelper.ToDictionaryList(data, true));
        }

        [HttpGet("GetBuildings")]
        [RequirePermission("properties:view")]
        public async Task<IActionResult> GetBuildings()
        {
            var data = await _service.GetBuildings();
            return Ok(DataTableHelper.ToDictionaryList(data, true));
        }

        [HttpGet("GetFloorsByBuilding/{buildingId}")]
        [RequirePermission("properties:view")]
        public async Task<IActionResult> GetFloorsByBuilding(int buildingId)
        {
            var data = await _service.GetFloorsByBuilding(buildingId);
            return Ok(DataTableHelper.ToDictionaryList(data, true));
        }

        [HttpGet("GetUnitsByFloor/{floorId}")]
        [RequirePermission("properties:view")]
        public async Task<IActionResult> GetUnitsByFloor(int floorId)
        {
            var data = await _service.GetUnitsByFloor(floorId);
            return Ok(DataTableHelper.ToDictionaryList(data, true));
        }

        [HttpGet("GetUnitsStatus")]
        [RequirePermission("properties:view")]
        public async Task<IActionResult> GetUnitsStatus()
        {
            var data = await _service.GetUnitsStatus();
            return Ok(DataTableHelper.ToDictionaryList(data, true));
        }

        [HttpGet("GetCities")]
        [RequirePermission("properties:view")]
        public async Task<IActionResult> GetCities()
        {
            var data = await _service.GetCities();
            return Ok(DataTableHelper.ToDictionaryList(data, true));
        }

        [HttpGet("GetBuildingTypes")]
        [RequirePermission("properties:view")]
        public async Task<IActionResult> GetBuildingTypes()
        {
            var data = await _service.GetBuildingTypes();
            return Ok(DataTableHelper.ToDictionaryList(data, true));
        }

        // ---------------- BUILDING ----------------

        [HttpPost("SaveBuilding")]
        [RequirePermission("properties:edit")]
        public async Task<IActionResult> SaveBuilding([FromBody] UpdatePropertyRequest req)
        {
            var response = await _service.SaveBuilding(req.buildingName, req.cityId, req.typeId, req.address);
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpPut("UpdateBuilding/{id}")]
        [RequirePermission("properties:edit")]
        public async Task<IActionResult> UpdateBuilding(int id, [FromBody] UpdatePropertyRequest req)
        {
            var response = await _service.UpdateBuilding(id, req.buildingName, req.cityId, req.typeId, req.address);
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpDelete("DeleteBuilding/{id}")]
        [RequirePermission("properties:edit")]
        [RequirePermission("action:deleteCancel")]
        public async Task<IActionResult> DeleteBuilding(int id)
        {
            var response = await _service.DeleteBuilding(id);
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        // ---------------- FLOOR ----------------

        [HttpPost("SaveFloor")]
        [RequirePermission("properties:edit")]
        public async Task<IActionResult> SaveFloor([FromBody] UpdatePropertyRequest req)
        {
            var response = await _service.SaveFloor(req.buildingId, req.floorNumber ?? "");
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpPut("UpdateFloor/{id}")]
        [RequirePermission("properties:edit")]
        public async Task<IActionResult> UpdateFloor(int id, [FromBody] UpdatePropertyRequest req)
        {
            var response = await _service.UpdateFloor(id, req.floorNumber ?? "");
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpDelete("DeleteFloor/{id}")]
        [RequirePermission("properties:edit")]
        [RequirePermission("action:deleteCancel")]
        public async Task<IActionResult> DeleteFloor(int id)
        {
            var response = await _service.DeleteFloor(id);
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        // ---------------- UNIT ----------------

        [HttpPost("SaveUnit")]
        [RequirePermission("properties:edit")]
        public async Task<IActionResult> SaveUnit([FromBody] UpdatePropertyRequest req)
        {
            var response = await _service.SaveUnit(req.floorId, req.buildingId,
                req.unitNumber ?? "", req.statusId, req.baseRent, req.note, req.propertyType);

            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpPut("UpdateUnit")]
        [RequirePermission("properties:edit")]
        public async Task<IActionResult> UpdateUnit([FromBody] UpdatePropertyRequest req)
        {
            var response = await _service.UpdateUnit(req.unitId, req.floorId, req.buildingId,
                req.unitNumber ?? "", req.statusId, req.baseRent, req.note, req.propertyType);

            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpDelete("DeleteUnit/{unitId}")]
        [RequirePermission("properties:edit")]
        [RequirePermission("action:deleteCancel")]
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
            public string? floorNumber { get; set; } // text: "G", "1", "Mezzanine"
            public string? unitNumber { get; set; } // text: "101", "B1", "A-101"
            public int statusId { get; set; }
            public decimal baseRent { get; set; }
            public string? note { get; set; }
            public string? address { get; set; }
            public string? propertyType { get; set; }
        }
    }
}