using System.ComponentModel.DataAnnotations;

namespace TRL_API.Models
{
    public class Dashboard
    {
        [Key]
        public string? Month { get; set; }
        public int TenantCount { get; set; }
        public decimal TotalRent { get; set; }
        public decimal Collected { get; set; }
        public decimal Pending => TotalRent - Collected;
    }
}
