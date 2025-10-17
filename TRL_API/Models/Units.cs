using System.ComponentModel.DataAnnotations;

namespace TRL_API.Models
{
    public class Units
    {
        [Key]
        public int UnitId { get; set; }
        public int FloorId { get; set; }
        public string? UnitNumber { get; set; }
        public int BuildingId { get; set; }
        public bool? IsActive { get; set; }
        public Buildings? Building { get; set; } // optional navigation
        public ICollection<Tenants>? Tenants { get; set; }
    }
}