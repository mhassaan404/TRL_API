namespace TRL_API.Models
{
    // Security deposits (money held for a tenancy, not rent). Nullable fields so a missing value is reported as missing.

    // Money received for the lease's tenancy
    public class RecordDepositRequest
    {
        public int LeaseId { get; set; }
        public decimal? Amount { get; set; }
        public DateTime? EntryDate { get; set; }
        public string? PaymentMethod { get; set; }
        public string? Reference { get; set; } // cheque / bank / transaction number
        public string? Notes { get; set; }
    }

    // Fixes a wrongly recorded deposit: reduces the held amount, with a reason. Original entries are never edited or deleted.
    public class CorrectDepositRequest
    {
        public int TenantId { get; set; }
        public int UnitId { get; set; }
        public decimal? Amount { get; set; } // positive: how much to take off the held amount
        public DateTime? EntryDate { get; set; }
        public string? Reason { get; set; }
    }

    // The agreed deposit for a tenancy; null clears it
    public class SetDepositAgreedRequest
    {
        public int TenantId { get; set; }
        public int UnitId { get; set; }
        public decimal? AgreedAmount { get; set; }
    }
}
