namespace TRL_API.Models
{
    // Result of recording payments: the Ids of the payments that received money, for printing their receipts
    public class PaymentSubmitResponse : ApiResponse
    {
        public List<int> ReceiptIds { get; set; } = new();
    }
}
