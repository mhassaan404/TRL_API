namespace TRL_API.Models
{
    // Tenant Statement report (read only). Charges increase the balance, credits reduce it.
    public class TenantStatement
    {
        public int TenantId { get; set; }
        public string TenantName { get; set; } = "";
        public string? TenantType { get; set; }
        public string? ContactPerson { get; set; }
        public string? Contact { get; set; }
        public string? Email { get; set; }
        public string? CnicNtn { get; set; }
        public string? Address { get; set; }
        public string? Units { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public decimal OpeningBalance { get; set; }
        public decimal TotalCharges { get; set; }
        public decimal TotalCredits { get; set; }
        public decimal ClosingBalance { get; set; }
        // Late fee building up on unpaid invoices today, not charged yet (so not in the balance)
        public decimal OpenLateFee { get; set; }
        public DateTime AsOfDate { get; set; }
        public List<StatementLine> Lines { get; set; } = new();
    }

    public class StatementLine
    {
        public DateTime Date { get; set; }
        public string Type { get; set; } = ""; // Invoice, Late Fee, Payment, Discount
        public int? InvoiceId { get; set; }
        public int? PaymentId { get; set; }
        public string? Description { get; set; }
        public string? Unit { get; set; }
        public string? Method { get; set; }
        public DateTime? DueDate { get; set; }
        public decimal Charge { get; set; }
        public decimal Credit { get; set; }
        public decimal Balance { get; set; }
    }
}
