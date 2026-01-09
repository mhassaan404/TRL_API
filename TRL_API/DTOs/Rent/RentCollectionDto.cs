namespace TRL_API.DTOs.Rent
{
    public class RentCollectionDto
    {
        public int InvoiceId { get; set; }
        public string TenantName { get; set; }
        public string BuildingName { get; set; }
        public int FloorNumber { get; set; }
        public string UnitNumber { get; set; }
        public string InvoiceMonth { get; set; }
        public decimal MonthlyRent { get; set; }
        public decimal PendingAmount { get; set; }
        public string Status { get; set; }
        public DateTime DueDate { get; set; }
        public decimal? LatestPaymentAmount { get; set; }
        public DateTime? LatestPaymentDate { get; set; }
        public string LatestPaymentMethod { get; set; }
    }
}
