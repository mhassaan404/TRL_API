namespace TRL_API.Models
{
    // New job or edit of an existing one (Id > 0). Status changes go through ChangeMaintenanceStatusRequest.
    public class MaintenanceJobRequest
    {
        public int Id { get; set; }
        public int BuildingId { get; set; }
        public int? FloorId { get; set; }
        public int? UnitId { get; set; }
        public int? TenantId { get; set; }
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public string Category { get; set; } = "";
        public string Priority { get; set; } = "Medium";
        public string? AssignedTo { get; set; }
        public DateTime? ReportedDate { get; set; } // default today
        public decimal? Cost { get; set; }
        public bool MarkUnitUnderMaintenance { get; set; } // new jobs only
    }

    public class ChangeMaintenanceStatusRequest
    {
        public int Id { get; set; }
        public string Status { get; set; } = "";
        public string? Note { get; set; }
        public DateTime? CompletedDate { get; set; } // for Completed; default today
    }

    // Bills the job's cost (or another amount) to the job's tenant as a Maintenance extra-charge invoice
    public class BillMaintenanceRequest
    {
        public int Id { get; set; }
        public decimal Amount { get; set; }
        public string? Description { get; set; }
        public int? DueInDays { get; set; } // null = Payment Due Days setting
        public bool ApplyLateFee { get; set; } = true;
    }
}
