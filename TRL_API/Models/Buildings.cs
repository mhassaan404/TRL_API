using System.ComponentModel.DataAnnotations;

namespace TRL_API.Models
{
    public class Buildings
    {
        [Key]
        public int BuildingId { get; set; }
        public string? BuildingName { get; set; }
        public string? Address { get; set; }
        public bool? IsActive { get; set; }
    }
}
