using TRL_API.DAL;
using TRL_API.Models;

namespace TRL_API.BLL
{
    public class LateFeeSettingsService : ILateFeeSettingsService
    {
        // Limits shared with the page and the database CHECK constraints
        public const int MaxDueDays = 90;
        public const decimal MaxFeePerDay = 100000;
        public const decimal MaxMultiplier = 12;

        private readonly ILateFeeSettingsRepository _dal;
        public LateFeeSettingsService(ILateFeeSettingsRepository dal) => _dal = dal;

        public async Task<LateFeeSettings> GetAsync() => await _dal.GetAsync();

        // Only changes the rules for invoices generated from now on; existing invoices keep their own due date,
        // rate and cap.
        public async Task<ApiResponse> SaveAsync(SaveLateFeeSettingsRequest req, int userId)
        {
            var error = Validate(req);
            if (error != null)
                return new ApiResponse { IsSuccess = false, ErrorMessage = error };

            var result = await _dal.SaveAsync(req.PaymentDueDays!.Value, req.LateFeePerDay!.Value, req.MaxLateFeeMultiplier!.Value, userId);
            return result.IsSuccess
                ? new ApiResponse { IsSuccess = true, Message = "Late fee settings saved. They apply to invoices generated from now on." }
                : new ApiResponse { IsSuccess = false, ErrorMessage = "Late fee settings could not be saved." };
        }

        public static string? Validate(SaveLateFeeSettingsRequest? req)
        {
            if (req == null)
                return "Please enter the late fee settings.";

            if (req.PaymentDueDays == null)
                return "Please enter the payment due days.";
            if (req.PaymentDueDays < 0 || req.PaymentDueDays > MaxDueDays)
                return $"Payment due days must be a whole number from 0 to {MaxDueDays}.";

            if (req.LateFeePerDay == null)
                return "Please enter the late fee per day.";
            if (req.LateFeePerDay <= 0 || req.LateFeePerDay > MaxFeePerDay || req.LateFeePerDay != decimal.Truncate(req.LateFeePerDay.Value))
                return $"Late fee per day must be a whole rupee amount from 1 to {MaxFeePerDay:N0}.";

            if (req.MaxLateFeeMultiplier == null)
                return "Please enter the maximum late fee.";
            if (req.MaxLateFeeMultiplier <= 0 || req.MaxLateFeeMultiplier > MaxMultiplier)
                return $"Maximum late fee must be greater than 0 and at most {MaxMultiplier:0} times the invoice rent.";
            if (req.MaxLateFeeMultiplier * 10 != decimal.Truncate(req.MaxLateFeeMultiplier.Value * 10))
                return "Maximum late fee can have at most one decimal place (e.g. 1.5).";

            return null;
        }
    }
}
