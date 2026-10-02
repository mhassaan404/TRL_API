namespace TRL_API.Models
{
    // Due-date and late-fee rules (one stored row, edited on the Late Fee Settings page).
    // New invoices copy LateFeePerDay / MaxLateFeeMultiplier, so changing them never re-prices existing invoices.
    public class LateFeeSettings
    {
        public int PaymentDueDays { get; set; }          // due date = invoice date + this many days
        public decimal LateFeePerDay { get; set; }        // charged for each day unpaid after the due date
        public decimal MaxLateFeeMultiplier { get; set; } // late fee stops growing at this multiple of the invoice rent
        public string? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // Nullable so a missing field is reported as missing instead of being read as 0
    public class SaveLateFeeSettingsRequest
    {
        public int? PaymentDueDays { get; set; }
        public decimal? LateFeePerDay { get; set; }
        public decimal? MaxLateFeeMultiplier { get; set; }
    }
}
