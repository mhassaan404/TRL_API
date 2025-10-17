using System.ComponentModel.DataAnnotations;

namespace TRL_API.Models
{
    public class Tenants
    {
        [Key]
        public int Id { get; set; }
        public string? Name { get; set; }
        public int BuildingId { get; set; }
        public int FloorId { get; set; }
        public int UnitId { get; set; }
        public string? Contact { get; set; }
        public DateTime? CreatedAt { get; set; }
        public decimal? MonthlyRent { get; set; }
        public bool? IsActive { get; set; }
        public Units? Unit { get; set; }
    }
}
