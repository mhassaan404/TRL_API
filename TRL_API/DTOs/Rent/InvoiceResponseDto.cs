namespace TRL_API.DTOs.Rent
{
    public class InvoiceResponseDto
    {
        public List<InvoiceRowDto> Invoices { get; set; } = new();
        public InvoiceSummaryDto Summary { get; set; } = new();
    }

    public class InvoiceRowDto
    {
        public int InvoiceId { get; set; }
        public int? PaymentId { get; set; }
        public int TenantId { get; set; }
        public string TenantName { get; set; } = string.Empty;
        public string BuildingName { get; set; } = string.Empty;
        public string FloorNumber { get; set; } = string.Empty;
        public string UnitNumber { get; set; } = string.Empty;

        public DateTime InvoiceDate { get; set; }
        public DateTime DueDate { get; set; }

        public decimal MonthlyRent { get; set; }
        public decimal RemainingAmount { get; set; }
        public int StatusId { get; set; }
        public string StatusName { get; set; } = string.Empty;

        public decimal PaidAmount { get; set; }
        public decimal AppliedDiscount { get; set; }
        public decimal PayAmount { get; set; }
        public DateTime? PaymentDate { get; set; }
        public string? PaymentMethod { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }

        public decimal DiscountAmount { get; set; }
        public decimal DiscountPercent { get; set; }
        public bool IsLateFeeWaived { get; set; }

        public decimal LateFee { get; set; }
    }

    public class InvoiceSummaryDto
    {
        public decimal MonthlyRent { get; set; }
        public decimal Pending { get; set; }
        public decimal PreviousBalance { get; set; }
        public decimal TotalLateFee { get; set; }
    }
}
