using System.ComponentModel.DataAnnotations;

namespace TRL_API.Models
{
    public class Payments
    {
        [Key]
        public int Id { get; set; }
        [Required]
        public int TenantId { get; set; }
        [Required]
        public decimal PaymentAmount { get; set; }
        [Required]
        public DateTime PaymentDate { get; set; }
        [Required]
        public int RentInvoiceId { get; set; }
        [Required]
        public string? PaymentMethod { get; set; }
        public DateTime? CreatedAt { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int? UpdatedBy { get; set; }
        public string? Notes { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal DiscountPercent { get; set; }
        public bool IsLateFeeWaived { get; set; }
    }
}
