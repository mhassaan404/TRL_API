using System.Data;
using TRL_API.DAL;
using TRL_API.Models;

namespace TRL_API.BLL
{
    public class MaintenanceService : IMaintenanceService
    {
        public static readonly string[] Categories = { "Plumbing", "Electrical", "AC", "Carpentry", "Painting", "Cleaning", "Other" };
        public static readonly string[] Priorities = { "Low", "Medium", "High", "Urgent" };
        public static readonly string[] Statuses = { "Open", "In Progress", "Completed", "Cancelled" };

        private readonly IMaintenanceRepository _dal;
        private readonly ILateFeeSettingsRepository _lateFeeSettings;

        public MaintenanceService(IMaintenanceRepository dal, ILateFeeSettingsRepository lateFeeSettings)
        {
            _dal = dal;
            _lateFeeSettings = lateFeeSettings;
        }

        public Task<DataTable> GetAllAsync() => _dal.GetAllAsync();
        public Task<DataTable> GetLogAsync(int jobId) => _dal.GetLogAsync(jobId);
        public Task<DataTable> GetBuildingsAsync() => _dal.GetBuildingsAsync();
        public Task<DataTable> GetUnitsAsync() => _dal.GetUnitsAsync();

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static bool BadMoney(decimal v) => v != decimal.Round(v, 2);

        // Input checks for a new or edited job (the location/tenant checks run in the database)
        public static string? ValidateJob(MaintenanceJobRequest? req, DateTime today)
        {
            if (req == null) return "Please enter the job.";
            if (req.Id < 0) return "The job is invalid.";
            if (req.BuildingId <= 0) return "Please select a building.";
            if (req.FloorId is <= 0 || req.UnitId is <= 0 || req.TenantId is <= 0) return "The selected floor, unit or tenant is invalid.";
            if (req.UnitId != null && req.FloorId == null) return "Please select the unit's floor.";
            var title = req.Title?.Trim() ?? "";
            if (title.Length == 0) return "Please enter a title.";
            if (title.Length > 150) return "Title can be at most 150 characters.";
            if ((req.Description?.Trim().Length ?? 0) > 1000) return "Description can be at most 1,000 characters.";
            if (!Categories.Contains(req.Category)) return $"Category must be one of: {string.Join(", ", Categories)}.";
            if (!Priorities.Contains(req.Priority)) return $"Priority must be one of: {string.Join(", ", Priorities)}.";
            if ((req.AssignedTo?.Trim().Length ?? 0) > 100) return "Assigned to can be at most 100 characters.";
            if (req.Cost is < 0 or > RentService.MaxExtraChargeAmount) return $"Cost must be between 0 and {RentService.MaxExtraChargeAmount:N0}.";
            if (req.Cost is decimal c && BadMoney(c)) return "Cost can have at most 2 decimal places.";
            var reported = (req.ReportedDate ?? today).Date;
            if (reported > today) return "The reported date can't be in the future.";
            if (reported < today.AddYears(-1)) return "The reported date must be within the last 12 months.";
            if (req.MarkUnitUnderMaintenance && req.UnitId == null) return "Select a unit to mark it Under Maintenance.";
            return null;
        }

        public async Task<ApiResponse> SaveAsync(MaintenanceJobRequest req, int userId)
        {
            var error = ValidateJob(req, DateTime.Today);
            if (error != null) return Fail(error);

            req.Title = req.Title.Trim();
            req.Description = Clean(req.Description);
            req.AssignedTo = Clean(req.AssignedTo);
            var reported = (req.ReportedDate ?? DateTime.Today).Date;

            if (req.Id == 0)
            {
                var (result, id) = await _dal.CreateAsync(req, reported, userId);
                return result == "OK"
                    ? new ApiResponse { IsSuccess = true, Id = id, Message = $"Maintenance job #{id} created." }
                    : Fail(Message(result));
            }

            req.MarkUnitUnderMaintenance = false; // only offered when creating a job
            var updated = await _dal.UpdateAsync(req, reported, userId);
            return updated == "OK"
                ? new ApiResponse { IsSuccess = true, Id = req.Id, Message = $"Maintenance job #{req.Id} updated." }
                : Fail(Message(updated));
        }

        public async Task<ApiResponse> ChangeStatusAsync(ChangeMaintenanceStatusRequest req, int userId)
        {
            if (req == null || req.Id <= 0) return Fail("The job is invalid.");
            if (!Statuses.Contains(req.Status)) return Fail($"Status must be one of: {string.Join(", ", Statuses)}.");
            var note = Clean(req.Note);
            if (note?.Length > 500) return Fail("Note can be at most 500 characters.");
            if (req.Status == "Cancelled" && note == null) return Fail("Please enter a reason for cancelling the job.");
            var completed = (req.CompletedDate ?? DateTime.Today).Date;
            if (req.Status == "Completed" && completed > DateTime.Today) return Fail("The completed date can't be in the future.");

            var result = await _dal.ChangeStatusAsync(req.Id, req.Status, note, req.Status == "Completed" ? completed : null, userId);
            return result == "OK"
                ? new ApiResponse { IsSuccess = true, Message = $"Job #{req.Id} is now {req.Status}." }
                : Fail(Message(result));
        }

        public async Task<ApiResponse> BillAsync(BillMaintenanceRequest req, int userId)
        {
            if (req == null || req.Id <= 0) return Fail("The job is invalid.");
            if (req.Amount <= 0 || req.Amount > RentService.MaxExtraChargeAmount)
                return Fail($"Amount must be greater than 0 and at most {RentService.MaxExtraChargeAmount:N0}.");
            if (BadMoney(req.Amount)) return Fail("Amount can have at most 2 decimal places.");
            var description = Clean(req.Description);
            if (description?.Length > 255) return Fail("Description can be at most 255 characters.");
            if (req.DueInDays is < 0 or > 90) return Fail("Due days must be between 0 and 90.");

            var settings = await _lateFeeSettings.GetAsync();
            var dueDate = DateTime.Today.AddDays(req.DueInDays ?? settings.PaymentDueDays);
            var (result, invoiceId) = await _dal.BillAsync(req.Id, req.Amount, description ?? $"Maintenance job #{req.Id}", dueDate,
                req.ApplyLateFee ? settings.LateFeePerDay : 0, settings.MaxLateFeeMultiplier, userId);

            return result == "OK"
                ? new ApiResponse
                {
                    IsSuccess = true,
                    Id = invoiceId,
                    Message = $"Billed to the tenant as invoice #{invoiceId} ({req.Amount:N0}), due {dueDate:dd MMM yyyy}.",
                }
                : Fail(result == "ALREADY_BILLED" ? $"This job was already billed as invoice #{invoiceId}." : Message(result));
        }

        private static ApiResponse Fail(string message) => new() { IsSuccess = false, ErrorMessage = message };

        private static string Message(string result) => result switch
        {
            "NOT_FOUND" => "Maintenance job not found.",
            "BAD_BUILDING" => "The selected building doesn't exist or is inactive.",
            "BAD_FLOOR" => "The selected floor isn't in that building or is inactive.",
            "BAD_UNIT" => "The selected unit isn't on that floor or is inactive.",
            "BAD_TENANT" => "The selected tenant doesn't exist.",
            "CANCELLED" => "This job was cancelled, so it can't be changed.",
            "LOCKED_BILLED" => "This job was billed to the tenant, so its location and tenant can't be changed.",
            "LOCKED_MARKED" => "This job set the unit Under Maintenance, so its location and tenant can't be changed until it is closed.",
            "FINAL" => "This job is already closed (Completed or Cancelled).",
            "NO_CHANGE" => "The job already has this status.",
            "BEFORE_REPORTED" => "The completed date can't be before the reported date.",
            "NO_TENANT" => "This job has no tenant. Edit it and select the tenant to bill.",
            _ => "The maintenance job could not be saved.",
        };
    }
}
