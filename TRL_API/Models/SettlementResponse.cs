namespace TRL_API.Models
{
    // Result of a move-out settlement: its Id, and the receipts of any money received from the tenant at settlement
    public class SettlementResponse : ApiResponse
    {
        public List<int> ReceiptIds { get; set; } = new();
    }
}
