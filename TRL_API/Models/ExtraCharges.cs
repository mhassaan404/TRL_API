namespace TRL_API.Models
{
    // One-time extra charge: a separate invoice per tenant (ChargeType set, no lease). The invoice it relates to,
    // if any, is never changed.
    public class ExtraChargeRequest
    {
        public List<int> TenantIds { get; set; } = new();
        public DateTime? ChargeDate { get; set; }   // invoice date; null = today
        public int? DueInDays { get; set; }         // null = the Payment Due Days setting
        public string ChargeType { get; set; } = "";
        public string? Description { get; set; }
        public decimal Amount { get; set; }
        public int? RelatedInvoiceId { get; set; }  // e.g. the rent invoice a Rent Correction is for (one tenant only)
        public bool ApplyLateFee { get; set; } = true;
    }
}
