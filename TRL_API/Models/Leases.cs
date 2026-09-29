namespace TRL_API.Models
{
    public class Lease
    {
        public int LeaseId { get; set; }
        public int TenantId { get; set; }
        public int UnitId { get; set; }
        public decimal RentAmount { get; set; }
        public DateTime StartDate { get; set; }
        public int TenureMonths { get; set; }
    }

    public class RenewLeaseRequest
    {
        public int LeaseId { get; set; }
        public decimal? NewRentAmount { get; set; } // null = keep current rent
        public int TenureMonths { get; set; }
    }

    public class TerminateLeaseRequest
    {
        public int LeaseId { get; set; }
        public string Reason { get; set; } = "";
        public DateTime? MoveOutDate { get; set; } // last day the tenant occupies the unit; default today
    }
}
