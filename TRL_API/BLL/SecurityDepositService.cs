using System.Data;
using TRL_API.DAL;
using TRL_API.Models;

namespace TRL_API.BLL
{
    // Security deposits: money held for a tenancy, recorded apart from rent (no invoices, not income).
    public class SecurityDepositService : ISecurityDepositService
    {
        public static readonly string[] Methods = { "Cash", "Bank Transfer", "Cheque", "Online" };
        public const decimal MaxAmount = 1_000_000_000m;
        public const int MaxReference = 100;
        public const int MaxNotes = 500;
        private static readonly DateTime MinDate = new(2000, 1, 1);

        private readonly ISecurityDepositRepository _dal;
        public SecurityDepositService(ISecurityDepositRepository dal) => _dal = dal;

        public Task<DataTable> GetTenanciesAsync() => _dal.GetTenanciesAsync();
        public Task<DataTable> GetHistoryAsync(int tenantId, int unitId) => _dal.GetHistoryAsync(tenantId, unitId);

        public async Task<ApiResponse> RecordAsync(RecordDepositRequest r, int userId)
        {
            var error = ValidateRecord(r, DateTime.Today);
            if (error != null) return Fail(error);

            var (result, info) = await _dal.RecordAsync(r, userId);
            return result switch
            {
                "OK" => new ApiResponse { IsSuccess = true, Id = (int)info!.Value, Message = $"Security deposit of PKR {r.Amount:N0} recorded." },
                "NOT_FOUND" => Fail("Lease not found."),
                "TENANT_DELETED" => Fail("This tenant has been deleted."),
                "NOT_CURRENT" => Fail("This lease has ended or was cancelled. Deposits can only be recorded for a current or upcoming lease."),
                "DUPLICATE" => Fail("This deposit was already recorded a moment ago."),
                "OVER_AGREED" => Fail(info > 0
                    ? $"This is more than the agreed deposit. Only PKR {info:N0} is still due."
                    : "The agreed deposit has already been received in full."),
                "BUSY" => Fail("Another deposit for this tenancy is being saved. Please try again."),
                _ => Fail("The deposit could not be recorded."),
            };
        }

        public async Task<ApiResponse> CorrectAsync(CorrectDepositRequest r, int userId)
        {
            var error = ValidateCorrection(r, DateTime.Today);
            if (error != null) return Fail(error);

            var (result, info) = await _dal.CorrectAsync(r, userId);
            return result switch
            {
                "OK" => new ApiResponse { IsSuccess = true, Id = (int)info!.Value, Message = $"Correction of PKR {r.Amount:N0} recorded." },
                "NOT_FOUND" => Fail("No lease found for this tenant and unit."),
                "OVER_HELD" => Fail($"A correction can't be more than the deposit held (PKR {info:N0})."),
                "DUPLICATE" => Fail("This correction was already recorded a moment ago."),
                "BUSY" => Fail("Another deposit for this tenancy is being saved. Please try again."),
                _ => Fail("The correction could not be recorded."),
            };
        }

        public async Task<ApiResponse> SetAgreedAsync(SetDepositAgreedRequest r, int userId)
        {
            var error = ValidateAgreed(r);
            if (error != null) return Fail(error);

            var (result, info) = await _dal.SetAgreedAsync(r, userId);
            return result switch
            {
                "OK" => new ApiResponse { IsSuccess = true, Message = r.AgreedAmount == null ? "Agreed deposit cleared." : $"Agreed deposit set to PKR {r.AgreedAmount:N0}." },
                "NOT_FOUND" => Fail("No lease found for this tenant and unit."),
                "BELOW_HELD" => Fail($"The agreed deposit can't be less than the deposit already held (PKR {info:N0})."),
                "BUSY" => Fail("Another deposit for this tenancy is being saved. Please try again."),
                _ => Fail("The agreed deposit could not be saved."),
            };
        }

        public async Task<(DataTable? Receipt, string? Error)> GetReceiptAsync(int depositId)
        {
            var dt = await _dal.GetReceiptAsync(depositId);
            if (dt.Rows.Count == 0) return (null, $"Deposit #{depositId} was not found.");
            if (dt.Rows[0]["EntryType"].ToString() != "Received") return (null, $"Deposit entry #{depositId} is a correction and has no receipt.");
            return (dt, null);
        }

        // ---- Validation (static so it can be unit tested). Cleans text fields in place. ----

        public static string? ValidateRecord(RecordDepositRequest? r, DateTime today)
        {
            if (r == null) return "Please enter the deposit details.";
            if (r.LeaseId <= 0) return "Please choose a lease.";
            var amount = CheckAmount(r.Amount, "deposit amount");
            if (amount != null) return amount;
            var date = CheckDate(r.EntryDate, today, "received date");
            if (date != null) return date;
            r.PaymentMethod = Clean(r.PaymentMethod);
            if (r.PaymentMethod == null) return "Please choose the payment method.";
            var method = Methods.FirstOrDefault(m => string.Equals(m, r.PaymentMethod, StringComparison.OrdinalIgnoreCase));
            if (method == null) return $"Payment method must be one of: {string.Join(", ", Methods)}.";
            r.PaymentMethod = method;
            r.Reference = Clean(r.Reference);
            if (r.Reference?.Length > MaxReference) return $"Reference can be at most {MaxReference} characters.";
            r.Notes = Clean(r.Notes);
            if (r.Notes?.Length > MaxNotes) return $"Notes can be at most {MaxNotes} characters.";
            return null;
        }

        public static string? ValidateCorrection(CorrectDepositRequest? r, DateTime today)
        {
            if (r == null) return "Please enter the correction details.";
            if (r.TenantId <= 0 || r.UnitId <= 0) return "Please choose a tenancy.";
            var amount = CheckAmount(r.Amount, "correction amount");
            if (amount != null) return amount;
            var date = CheckDate(r.EntryDate, today, "correction date");
            if (date != null) return date;
            r.Reason = Clean(r.Reason);
            if (r.Reason == null) return "Please enter the reason for the correction.";
            if (r.Reason.Length > MaxNotes) return $"Reason can be at most {MaxNotes} characters.";
            return null;
        }

        public static string? ValidateAgreed(SetDepositAgreedRequest? r)
        {
            if (r == null) return "Please enter the agreed deposit.";
            if (r.TenantId <= 0 || r.UnitId <= 0) return "Please choose a tenancy.";
            return r.AgreedAmount == null ? null : CheckAmount(r.AgreedAmount, "agreed deposit");
        }

        private static string? CheckAmount(decimal? amount, string what)
        {
            if (amount == null) return $"Please enter the {what}.";
            if (amount <= 0) return $"The {what} must be greater than 0.";
            if (amount > MaxAmount) return $"The {what} can be at most {MaxAmount:N0}.";
            if (amount * 100 != decimal.Truncate(amount.Value * 100)) return $"The {what} can have at most 2 decimal places.";
            return null;
        }

        // Not in the future. One day of slack so a date entered in Pakistan (UTC+5) is never refused by a server
        // running on UTC just after midnight.
        private static string? CheckDate(DateTime? date, DateTime today, string what)
        {
            if (date == null) return $"Please enter the {what}.";
            if (date.Value.Date < MinDate) return $"The {what} must be in 2000 or later.";
            if (date.Value.Date > today.Date.AddDays(1)) return $"The {what} can't be in the future.";
            return null;
        }

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        private static ApiResponse Fail(string message) => new() { IsSuccess = false, ErrorMessage = message };
    }
}
