using TRL_API.BLL;
using TRL_API.Models;
using Xunit;

namespace TRL_API.Tests
{
    // Maintenance job input checks (MaintenanceService.ValidateJob). Location/tenant checks run in SQL.
    public class MaintenanceValidationTests
    {
        private static readonly DateTime Today = new(2026, 10, 7);

        private static MaintenanceJobRequest Job(Action<MaintenanceJobRequest>? change = null)
        {
            var j = new MaintenanceJobRequest { BuildingId = 6, FloorId = 11, UnitId = 24, TenantId = 16, Title = "Leaking tap", Category = "Plumbing", Priority = "High", Cost = 1500 };
            change?.Invoke(j);
            return j;
        }

        [Fact]
        public void A_complete_job_is_valid()
        {
            Assert.Null(MaintenanceService.ValidateJob(Job(), Today));
            Assert.Null(MaintenanceService.ValidateJob(Job(j => { j.FloorId = null; j.UnitId = null; j.TenantId = null; j.Cost = null; }), Today)); // common area
            Assert.Null(MaintenanceService.ValidateJob(Job(j => { j.ReportedDate = Today.AddMonths(-11); j.MarkUnitUnderMaintenance = true; }), Today));
        }

        public static IEnumerable<object[]> Invalid() => new[]
        {
            new object[] { Job(j => j.BuildingId = 0), "Please select a building." },
            new object[] { Job(j => j.FloorId = null), "Please select the unit's floor." },
            new object[] { Job(j => j.TenantId = -1), "The selected floor, unit or tenant is invalid." },
            new object[] { Job(j => j.Title = " "), "Please enter a title." },
            new object[] { Job(j => j.Title = new string('x', 151)), "Title can be at most 150 characters." },
            new object[] { Job(j => j.Description = new string('x', 1001)), "Description can be at most 1,000 characters." },
            new object[] { Job(j => j.Category = "Roof"), "Category must be one of: Plumbing, Electrical, AC, Carpentry, Painting, Cleaning, Other." },
            new object[] { Job(j => j.Priority = "Asap"), "Priority must be one of: Low, Medium, High, Urgent." },
            new object[] { Job(j => j.AssignedTo = new string('x', 101)), "Assigned to can be at most 100 characters." },
            new object[] { Job(j => j.Cost = -1), "Cost must be between 0 and 10,000,000." },
            new object[] { Job(j => j.Cost = 1.005m), "Cost can have at most 2 decimal places." },
            new object[] { Job(j => j.ReportedDate = Today.AddDays(1)), "The reported date can't be in the future." },
            new object[] { Job(j => j.ReportedDate = Today.AddYears(-1).AddDays(-1)), "The reported date must be within the last 12 months." },
            new object[] { Job(j => { j.UnitId = null; j.MarkUnitUnderMaintenance = true; }), "Select a unit to mark it Under Maintenance." },
        };

        [Theory]
        [MemberData(nameof(Invalid))]
        public void Invalid_jobs_are_rejected_with_a_clear_message(MaintenanceJobRequest job, string message) =>
            Assert.Equal(message, MaintenanceService.ValidateJob(job, Today));
    }
}
