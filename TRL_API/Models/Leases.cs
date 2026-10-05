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

    // Undo an early renewal that hasn't started: the renewal is cancelled and the current term carries on
    public class CancelRenewalRequest
    {
        public int LeaseId { get; set; } // the upcoming (renewal) lease
    }

    // Correct an open lease's start date, rent or tenure (Edit on Lease Management)
    public class UpdateLeaseRequest
    {
        public int LeaseId { get; set; }
        public DateTime? StartDate { get; set; }
        public decimal? RentAmount { get; set; }
        public int? TenureMonths { get; set; }
    }

    // Undo a lease created by mistake: it is cancelled and never billed
    public class CancelLeaseRequest
    {
        public int LeaseId { get; set; }
    }
}
