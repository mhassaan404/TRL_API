using System.Data;
using TRL_API.DAL;
using TRL_API.Models;

namespace TRL_API.BLL
{
    // Move-out settlement: after a lease has ended, the security deposit (and any tenant credit) pays what the tenant
    // still owes, justified deductions are billed, and whatever is left is either still owed by the tenant or refundable.
    public class SettlementService : ISettlementService
    {
        public static readonly string[] DeductionTypes = { "Damage", "Cleaning", "Maintenance", "Utility", "Other" };
        public const int MaxDeductions = 20;
        public const int MaxReason = 255;
        public const decimal MaxAmount = 1_000_000_000m;

        private readonly ISettlementRepository _dal;
        public SettlementService(ISettlementRepository dal) => _dal = dal;

        public Task<DataTable> GetCandidatesAsync() => _dal.GetCandidatesAsync();
        public Task<DataTable> GetSettlementsAsync() => _dal.GetSettlementsAsync();

        public async Task<(DataTable Header, DataTable Invoices, DataTable Unbilled)?> GetPreviewAsync(int tenantId, int unitId)
        {
            var header = await _dal.GetPreviewHeaderAsync(tenantId, unitId);
            if (header.Rows.Count == 0 || header.Rows[0]["LeaseId"] == DBNull.Value) return null;
            return (header, await _dal.GetOpenInvoicesAsync(tenantId, unitId), await _dal.GetUnbilledMonthsAsync(tenantId, unitId));
        }

        public async Task<ApiResponse> FinalizeAsync(FinalizeSettlementRequest r, int userId)
        {
            var error = ValidateFinalize(r, DateTime.Today);
            if (error != null) return Fail(error);

            var (result, id, extra) = await _dal.FinalizeAsync(r, userId);
            return result switch
            {
                "OK" => new SettlementResponse
                {
                    IsSuccess = true, Id = id, Message = $"Move-out settlement #{id} recorded.",
                    ReceiptIds = string.IsNullOrEmpty(extra) ? new List<int>() : extra.Split(',').Select(int.Parse).ToList(),
                },
                "NOT_FOUND" => Fail("No lease found for this tenant and unit."),
                "LEASE_ACTIVE" => Fail("This tenancy still has a current or upcoming lease. End the lease in Lease Management first."),
                "ALREADY_SETTLED" => Fail($"This tenancy has already been settled (settlement #{id})."),
                "CHANGED" => Fail("The tenant's invoices, payments or deposit changed after you opened this settlement. Please review it again."),
                "DATE_BEFORE_MOVE_OUT" => Fail($"The settlement date can't be before the move-out date ({extra})."),
                "UNBILLED" => Fail($"{id} month(s) of rent covered by the lease have no invoice. Confirm this is intended, or bill them first."),
                "FINAL_TOO_HIGH" => Fail($"The payment received can't be more than what the tenant owes after the deposit (PKR {decimal.Parse(extra!):N0})."),
                "REFUND_TOO_HIGH" => Fail($"The refund can't be more than the refundable amount (PKR {decimal.Parse(extra!):N0})."),
                "NEGATIVE_HELD" => Fail("The deposit held for this tenancy is below zero. Correct the deposit history first."),
                "BUSY" => Fail("Another change for this tenancy is being saved. Please try again."),
                _ => Fail("The settlement could not be recorded."),
            };
        }

        public async Task<ApiResponse> RecordRefundAsync(RecordSettlementRefundRequest r, int userId)
        {
            var error = ValidateRefund(r, DateTime.Today);
            if (error != null) return Fail(error);

            var (result, info) = await _dal.RecordRefundAsync(r, userId);
            return result switch
            {
                "OK" => new ApiResponse { IsSuccess = true, Message = $"Refund of PKR {r.Amount:N0} recorded." },
                "NOT_FOUND" => Fail("Settlement not found."),
                "TOO_HIGH" => Fail(info > 0 ? $"The refund can't be more than PKR {info:N0} (still refundable and held)." : "Nothing is left to refund for this settlement."),
                "DATE_BEFORE" => Fail("The refund date can't be before the settlement date."),
                "DUPLICATE" => Fail("This refund was already recorded a moment ago."),
                "BUSY" => Fail("Another change for this tenancy is being saved. Please try again."),
                _ => Fail("The refund could not be recorded."),
            };
        }

        public async Task<(DataTable Header, DataTable Lines, DataTable Refunds)?> GetSettlementAsync(int id)
        {
            var header = await _dal.GetSettlementAsync(id);
            if (header.Rows.Count == 0) return null;
            return (header, await _dal.GetSettlementLinesAsync(id), await _dal.GetSettlementRefundsAsync(id));
        }

        // ---- Validation (static so it can be unit tested). Cleans text fields in place. ----

        public static string? ValidateFinalize(FinalizeSettlementRequest? r, DateTime today)
        {
            if (r == null) return "Please enter the settlement details.";
            if (r.TenantId <= 0 || r.UnitId <= 0 || r.LeaseId <= 0) return "Please choose the tenancy to settle.";
            if (r.ExpectedOutstanding == null || r.ExpectedCredit == null || r.ExpectedHeld == null)
                return "The reviewed figures are missing. Please open the settlement again.";
            var date = CheckDate(r.SettlementDate, today, "settlement date");
            if (date != null) return date;
            r.Notes = Clean(r.Notes);
            if (r.Notes?.Length > 500) return "Notes can be at most 500 characters.";

            var deductions = r.Deductions ?? new List<SettlementDeductionInput>();
            if (deductions.Count > MaxDeductions) return $"At most {MaxDeductions} deductions can be added.";
            for (var i = 0; i < deductions.Count; i++)
            {
                var d = deductions[i];
                var n = $"Deduction {i + 1}";
                if (d == null) return $"{n} is empty.";
                d.ChargeType = DeductionTypes.FirstOrDefault(t => string.Equals(t, Clean(d.ChargeType), StringComparison.OrdinalIgnoreCase));
                if (d.ChargeType == null) return $"{n}: type must be one of: {string.Join(", ", DeductionTypes)}.";
                var amount = CheckAmount(d.Amount, $"{n} amount");
                if (amount != null) return amount;
                d.Reason = Clean(d.Reason);
                if (d.Reason == null) return $"{n}: please enter the reason for the deduction.";
                if (d.Reason.Length > MaxReason) return $"{n}: the reason can be at most {MaxReason} characters.";
            }

            var final = CheckMoney(r.FinalPayment, "payment received");
            if (final != null) return final;
            var refund = CheckMoney(r.Refund, "refund paid");
            if (refund != null) return refund;
            return null;
        }

        public static string? ValidateRefund(RecordSettlementRefundRequest? r, DateTime today)
        {
            if (r == null) return "Please enter the refund details.";
            if (r.SettlementId <= 0) return "Please choose a settlement.";
            var amount = CheckAmount(r.Amount, "refund amount");
            if (amount != null) return amount;
            var date = CheckDate(r.RefundDate, today, "refund date");
            if (date != null) return date;
            var money = new SettlementMoneyInput { Amount = r.Amount, PaymentMethod = r.PaymentMethod, Reference = r.Reference };
            var error = CheckMoney(money, "refund");
            r.PaymentMethod = money.PaymentMethod;
            r.Reference = money.Reference;
            return error;
        }

        // Optional money block: when an amount is given it must be valid and say how it was paid
        private static string? CheckMoney(SettlementMoneyInput? m, string what)
        {
            if (m == null || m.Amount == null || m.Amount == 0)
            {
                if (m != null) { m.Amount = null; m.PaymentMethod = null; m.Reference = null; }
                return null;
            }
            var amount = CheckAmount(m.Amount, $"{what} amount");
            if (amount != null) return amount;
            m.PaymentMethod = SecurityDepositService.Methods.FirstOrDefault(x => string.Equals(x, Clean(m.PaymentMethod), StringComparison.OrdinalIgnoreCase));
            if (m.PaymentMethod == null) return $"Please choose the payment method for the {what} ({string.Join(", ", SecurityDepositService.Methods)}).";
            m.Reference = Clean(m.Reference);
            if (m.Reference?.Length > 100) return $"The {what} reference can be at most 100 characters.";
            return null;
        }

        private static string? CheckAmount(decimal? amount, string what)
        {
            if (amount == null) return $"Please enter the {what}.";
            if (amount <= 0) return $"The {what} must be greater than 0.";
            if (amount > MaxAmount) return $"The {what} can be at most {MaxAmount:N0}.";
            if (amount * 100 != decimal.Truncate(amount.Value * 100)) return $"The {what} can have at most 2 decimal places.";
            return null;
        }

        // Not in the future (one day of slack for the UTC+5 time difference)
        private static string? CheckDate(DateTime? date, DateTime today, string what)
        {
            if (date == null) return $"Please enter the {what}.";
            if (date.Value.Date < new DateTime(2000, 1, 1)) return $"The {what} must be in 2000 or later.";
            if (date.Value.Date > today.Date.AddDays(1)) return $"The {what} can't be in the future.";
            return null;
        }

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        private static ApiResponse Fail(string message) => new() { IsSuccess = false, ErrorMessage = message };
    }
}
