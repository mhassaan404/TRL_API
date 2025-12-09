using System.ComponentModel.DataAnnotations;

namespace TRL_API.Models
{
    public class Payments
    {
        [Key]
        public int Id { get; set; }
        public int TenantId { get; set; }
        public decimal PaymentAmount { get; set; }
        public DateTime? PaymentDate { get; set; }
        public int StatusId { get; set; }
        public int RentInvoiceId { get; set; }
        public string? PaymentMethod { get; set; }
        public string? Notes { get; set; }
        public DateTime? CreatedAt { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int UpdatedBy { get; set; }
    }
}
