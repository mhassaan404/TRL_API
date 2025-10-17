using System.ComponentModel.DataAnnotations;

namespace TRL_API.Models
{
    public class Floors
    {
        [Key]
        public int FloorId { get; set; }
        public int BuildingId { get; set; }
        public string? FloorNumber { get; set; }
        public bool? IsActive { get; set; }
    }
}
