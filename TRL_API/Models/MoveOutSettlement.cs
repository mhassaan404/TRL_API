namespace TRL_API.Models
{
    // Move-out settlement requests. Nullable fields so a missing value is reported as missing.

    public class SettlementDeductionInput
    {
        public string? ChargeType { get; set; } // Damage, Cleaning, Maintenance, Utility, Other
        public decimal? Amount { get; set; }
        public string? Reason { get; set; }
    }

    // Money that really changed hands at settlement (optional)
    public class SettlementMoneyInput
    {
        public decimal? Amount { get; set; }
        public string? PaymentMethod { get; set; }
        public string? Reference { get; set; }
    }

    public class FinalizeSettlementRequest
    {
        public int TenantId { get; set; }
        public int UnitId { get; set; }
        public int LeaseId { get; set; }                    // the ended lease the admin reviewed
        public decimal? ExpectedOutstanding { get; set; }   // figures the admin reviewed; refused if they changed
        public decimal? ExpectedCredit { get; set; }
        public decimal? ExpectedHeld { get; set; }
        public DateTime? SettlementDate { get; set; }
        public List<SettlementDeductionInput>? Deductions { get; set; }
        public SettlementMoneyInput? FinalPayment { get; set; } // received from the tenant now
        public SettlementMoneyInput? Refund { get; set; }       // paid back to the tenant now
        public bool AcknowledgeUnbilled { get; set; }           // admin confirmed un-invoiced rent months are intended
        public string? Notes { get; set; }
    }

    public class RecordSettlementRefundRequest
    {
        public int SettlementId { get; set; }
        public decimal? Amount { get; set; }
        public DateTime? RefundDate { get; set; }
        public string? PaymentMethod { get; set; }
        public string? Reference { get; set; }
    }
}
