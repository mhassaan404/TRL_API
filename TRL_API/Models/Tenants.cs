using System.ComponentModel.DataAnnotations;

namespace TRL_API.Models
{
    public class Tenants
    {
        [Key]
        public int TenantId { get; set; }
        public string? Name { get; set; }
        public int BuildingId { get; set; }
        public int FloorId { get; set; }
        public int UnitId { get; set; }
        public string? Contact { get; set; }
        public string? Email { get; set; }
        public decimal? MonthlyRent { get; set; }
        public DateTime? MoveOutDate { get; set; }
        public int CityId { get; set; }
        public string? Notes { get; set; }
        public int CreatedBy { get; set; }
        public DateTime? CreatedAt { get; set; }
        public int UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool? IsActive { get; set; }
    }
}
