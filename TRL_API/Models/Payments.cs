using System.ComponentModel.DataAnnotations;

namespace TRL_API.Models
{
    public class Payments
    {
        [Key]
        public int Id { get; set; }
        public int TenantId { get; set; }
        public decimal Amount { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime? PaymentDate { get; set; }
        public DateTime? CreatedAt { get; set; }
        public int? StatusId { get; set; }
        public Tenants? Tenant { get; set; } // navigation property
    }
}
