using System.Data;
using TRL_API.DAL;
using TRL_API.Helpers;
using TRL_API.Models;

namespace TRL_API.BLL
{
    public class RentService : IRentService
    {
        private readonly IRentRepository _dal;
        private readonly ILateFeeSettingsRepository _lateFeeSettings;

        public RentService(IRentRepository dal, ILateFeeSettingsRepository lateFeeSettings)
        {
            _dal = dal;
            _lateFeeSettings = lateFeeSettings;
        }

        public async Task<DataTable> GetTenantsAsync() => await _dal.GetTenantsAsync();

        public async Task<DataTable> GetInvoicesByTenantAsync(int tenantId) => await _dal.GetInvoicesByTenantAsync(tenantId);

        public async Task<DataTable> GetPaymentHistoryByIdAsync(int invoiceId) => await _dal.GetPaymentHistoryByIdAsync(invoiceId);

        public async Task<DataTable> GetRentCollectionAsync() => await _dal.GetRentCollectionAsync();

        public async Task<DataTable> GetTenantsWithRentAsync() => await _dal.GetTenantsWithRent();

        public async Task<DataTable> GetPaymentHistoryAsync(int invoiceId) => await _dal.GetPaymentHistoryAsync(invoiceId);

        // ---------------- UNPAID INVOICES + SUMMARY FOR A TENANT ----------------

        public async Task<object> GetUnpaidInvoicesByTenantAsync(int tenantId)
        {
            var dt = await _dal.GetUnpaidInvoiceByTenant(tenantId);
            var invoices = DataTableHelper.ToDictionaryList(dt, true);

            decimal monthlyRent = 0, pending = 0, previousBalance = 0, totalLateFee = 0;

            if (dt.Rows.Count > 0)
            {
                // Query orders by InvoiceDate DESC, so row 0 is the most recent invoice.
                monthlyRent = Convert.ToDecimal(dt.Rows[0]["MonthlyRent"]);
                totalLateFee = Convert.ToDecimal(dt.Rows[0]["TotalLateFeePerTenant"]);

                for (int i = 0; i < dt.Rows.Count; i++)
                {
                    var remaining = Convert.ToDecimal(dt.Rows[i]["RemainingAmount"]);
                    pending += remaining;

                    // "Previous balance" = arrears from every invoice except the
                    // most recent one (row 0).
                    if (i > 0)
                    {
                        previousBalance += remaining;
                    }
                }
            }

            var summary = new
            {
                monthlyRent,
                pending,
                previousBalance,
                totalLateFee,
            };

            return new { invoices, summary };
        }

        // ---------------- PAYMENTS ----------------

        // Methods a payment can be received by (same list as the payment screen). A row with no cash (discount or
        // waiver only) is sent as "Adjustment". The settlement methods ("Security Deposit", "Credit to Deposit") are
        // only written by the move-out settlement itself.
        public static readonly string[] CashMethods = { "Cash", "Bank Transfer", "Cheque", "Online" };
        public const string NoCashMethod = "Adjustment";
        public const decimal MaxPaymentAmount = 1_000_000_000m; // same limit as deposits and settlements
        public const int MaxPaymentsPerSubmit = 100;

        // Field checks for new payments before the database is touched (null = fine). Balances, tenants and the
        // overdue rule are checked against the invoice in ValidatePaymentAsync.
        public static string? ValidatePaymentFields(List<Payments> payments, DateTime today)
        {
            if (payments.Count > MaxPaymentsPerSubmit) return $"At most {MaxPaymentsPerSubmit} payments can be recorded at once.";
            // One row per invoice: the screen never sends two, and a second identical row would otherwise be refused as
            // "just recorded" by the double-submit check, with a misleading message
            var repeated = payments.GroupBy(p => p.RentInvoiceId).FirstOrDefault(g => g.Count() > 1);
            if (repeated != null) return $"Invoice #{repeated.Key} appears more than once in this payment. Record it once.";
            foreach (var p in payments)
            {
                var inv = $"invoice #{p.RentInvoiceId}";
                bool cash = p.PaymentAmount > 0;
                var method = p.PaymentMethod?.Trim();
                if (cash ? !CashMethods.Contains(method) : !(CashMethods.Contains(method) || method == NoCashMethod || string.IsNullOrEmpty(method)))
                    return $"Payment method for {inv} must be one of: {string.Join(", ", CashMethods)}.";
                if (p.PaymentDate.Date < new DateTime(2000, 1, 1) || p.PaymentDate.Date > today.AddDays(1))
                    return $"Payment date for {inv} can't be in the future.";
                if (p.PaymentAmount > MaxPaymentAmount || p.DiscountAmount > MaxPaymentAmount)
                    return $"Amount for {inv} is too large.";
                if (p.PaymentAmount != decimal.Round(p.PaymentAmount, 2) || p.DiscountAmount != decimal.Round(p.DiscountAmount, 2))
                    return $"Amounts for {inv} can have at most 2 decimal places.";
                if (p.DiscountPercent < 0 || p.DiscountPercent > 100) return $"Discount % for {inv} must be between 0 and 100.";
                if (p.Notes?.Length > 500) return $"Notes for {inv} can be at most 500 characters.";
                p.PaymentMethod = string.IsNullOrEmpty(method) ? NoCashMethod : method;
            }
            return null;
        }

        // Records new payments; all of them share one connection and transaction, so a failure rolls every one back.
        public async Task<ApiResponse> SubmitPaymentsAsync(List<Payments> payments, int userId)
        {
            // Business date (Pakistan) + 1 day of slack for a browser clock that is slightly ahead around midnight
            var fieldError = ValidatePaymentFields(payments, BusinessDate.Today);
            if (fieldError != null) return new ApiResponse { IsSuccess = false, Message = fieldError, ErrorMessage = fieldError };

            using var conn = await _dal.GetOpenConnectionAsync();
            using var transaction = conn.BeginTransaction();
            var receiptIds = new List<int>(); // payments with money received: each one has a printable receipt

            foreach (var payment in payments)
            {
                var err = await _dal.ValidatePaymentAsync(payment, conn, transaction);
                if (err != null)
                {
                    transaction.Rollback();
                    return new ApiResponse { IsSuccess = false, Message = err, ErrorMessage = err };
                }

                var insertResult = await _dal.CreateRentAsync(payment, userId, conn, transaction);
                if (!insertResult.IsSuccess)
                {
                    transaction.Rollback();
                    return new ApiResponse { IsSuccess = false, Message = $"Failed to record payment for invoice #{payment.RentInvoiceId}." };
                }
                if (payment.PaymentAmount > 0)
                    receiptIds.Add(await _dal.GetLatestPaymentIdAsync(payment.RentInvoiceId, userId, conn, transaction));

                var updateResult = await _dal.RecalcInvoiceAsync(payment.RentInvoiceId, conn, transaction);
                if (!updateResult.IsSuccess)
                {
                    transaction.Rollback();
                    return new ApiResponse { IsSuccess = false, Message = $"Failed to update invoice #{payment.RentInvoiceId} after payment." };
                }
            }

            transaction.Commit();
            return new PaymentSubmitResponse { IsSuccess = true, Message = "Payment recorded successfully.", ReceiptIds = receiptIds };
        }

        public async Task<DataTable> GetVacantUnitsAsync(int? includeUnitId) => await _dal.GetVacantUnitsAsync(includeUnitId);

        public async Task<ApiResponse> ReverseLateFeeAsync(int invoiceId, string reason, int userId)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Reason is required." };
            if (reason.Trim().Length > RentHistoryService.InvoiceAuditReasonMax)
                return new ApiResponse { IsSuccess = false, ErrorMessage = $"The reason can be at most {RentHistoryService.InvoiceAuditReasonMax} characters." };
            var dt = await _dal.ReverseLateFeeAsync(invoiceId, reason.Trim(), userId);
            return Convert.ToDecimal(dt.Rows[0]["Fee"]) > 0
                ? new ApiResponse { IsSuccess = true, Message = "Late fee reversed." }
                : new ApiResponse { IsSuccess = false, ErrorMessage = "Nothing to reverse (no charged fee, payments already cover it, or the invoice is part of a move-out settlement)." };
        }

        public async Task<ApiResponse> CreateRentAsync(List<Payments> payments, int userId) =>
            await SubmitPaymentsAsync(payments, userId);

        // ---------------- PAYMENT REVERSAL ----------------
        // Payments are never edited or deleted: a row entered by mistake is reversed as a whole (cash, discount and
        // waiver together) by a linked row, then the correct payment is recorded as a new one. A reversal means
        // "entered by mistake", not money paid back to the tenant.

        public const int ReversalReasonMaxLength = 500; // Payments.Notes

        // Request checks before the database is touched (null = fine)
        public static string? ValidateReversalRequest(int paymentId, string? reason)
        {
            if (paymentId <= 0) return "Payment is required.";
            if (string.IsNullOrWhiteSpace(reason)) return "Please enter a reason for the reversal.";
            if (reason.Trim().Length > ReversalReasonMaxLength) return $"The reason can be at most {ReversalReasonMaxLength} characters.";
            if (reason.Any(c => char.IsControl(c) && c != '\n' && c != '\r' && c != '\t')) return "The reason contains invalid characters.";
            return null;
        }

        // Message for each reason code of InvoiceSql.ReversalBlock
        public static string ReversalBlockMessage(string code, int paymentId) => code switch
        {
            "NOT_FOUND" => $"Payment #{paymentId} was not found.",
            "IS_REVERSAL" => "This record is itself a reversal and can't be reversed.",
            "ALREADY_REVERSED" => $"Payment #{paymentId} has already been reversed.",
            "SETTLEMENT" => "This record belongs to a finalized move-out settlement and can't be reversed here.",
            "ADJUSTMENT" => "Adjustments can't be reversed.",
            "NOTHING" => "This record has no amount, discount or waiver to reverse.",
            "CANCELLED" => "The invoice is cancelled, so its payments can't be reversed.",
            "NEGATIVE" => "An adjustment has already taken back part of this payment, so reversing it would leave a negative total.",
            _ => "The payment could not be reversed.",
        };

        public async Task<ApiResponse> ReversePaymentAsync(int paymentId, string? reason, int userId)
        {
            var err = ValidateReversalRequest(paymentId, reason);
            if (err != null) return new ApiResponse { IsSuccess = false, Message = err, ErrorMessage = err };

            DataTable dt;
            try
            {
                dt = await _dal.ReversePaymentAsync(paymentId, reason!.Trim(), userId);
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627)
            {
                // UX_Payments_ReversalOf: another reversal of the same row won the race
                var msg = ReversalBlockMessage("ALREADY_REVERSED", paymentId);
                return new ApiResponse { IsSuccess = false, Message = msg, ErrorMessage = msg };
            }

            var row = dt.Rows[0];
            var result = row["Result"].ToString()!;
            if (result != "OK")
            {
                var msg = ReversalBlockMessage(result, paymentId);
                return new ApiResponse { IsSuccess = false, Message = msg, ErrorMessage = msg };
            }

            decimal balance = Convert.ToDecimal(row["Balance"]);
            return new ApiResponse
            {
                IsSuccess = true,
                Id = Convert.ToInt32(row["ReversalId"]),
                Message = $"Payment #{paymentId} reversed. Invoice #{row["InvoiceId"]} is now {row["Status"]}"
                          + (balance > 0 ? $" with {balance:N0} due." : "."),
            };
        }

        public const int MaxDueDateInvoices = 500;

        // Request checks before the database is touched (null = fine)
        public static string? ValidateDueDateChange(List<int>? invoiceIds, DateTime newDueDate, DateTime today)
        {
            if (invoiceIds == null || invoiceIds.Count == 0) return "Select at least one invoice.";
            if (invoiceIds.Count > MaxDueDateInvoices) return $"Update at most {MaxDueDateInvoices} invoices at a time.";
            if (invoiceIds.Any(id => id <= 0)) return "One or more selected invoices are invalid.";
            if (newDueDate.Date < new DateTime(2000, 1, 1) || newDueDate.Date > today.AddYears(1))
                return "Please choose a due date no more than a year from today.";
            return null;
        }

        public async Task<ApiResponse> BulkUpdateDueDateAsync(List<int>? invoiceIds, DateTime newDueDate, int userId)
        {
            var err = ValidateDueDateChange(invoiceIds, newDueDate, BusinessDate.Today);
            if (err != null) return new ApiResponse { IsSuccess = false, Message = err, ErrorMessage = err };

            var row = (await _dal.BulkUpdateDueDateAsync(invoiceIds!, newDueDate, userId)).Rows[0];
            int updated = Convert.ToInt32(row["Updated"]), requested = Convert.ToInt32(row["Requested"]);
            const string skippedWhy = "only unpaid or partly paid invoices that are not part of a move-out settlement can be changed, and not to a date before the invoice date";
            if (updated == 0)
            {
                var msg = $"No due date was changed: {skippedWhy}.";
                return new ApiResponse { IsSuccess = false, Message = msg, ErrorMessage = msg };
            }
            return new ApiResponse
            {
                IsSuccess = true,
                RowsAffected = updated,
                Message = $"Due date updated for {updated} invoice(s)."
                          + (updated < requested ? $" {requested - updated} skipped: {skippedWhy}." : ""),
            };
        }

        public async Task<DataTable> GetActiveTenantsAsync() => await _dal.GetActiveTenantsList();

        // ---------------- BULK INVOICE GENERATION (manual button) ----------------

        // Bills rent from the leases covering the month: one invoice per lease, prorated for partial months.
        // tenantIds: null or empty = all tenants. Otherwise only those tenants' leases.
        // Leases already billed this month are skipped, so re-running is always safe.
        // dueInDays: null = the Payment Due Days setting (an explicit value is still accepted, as before)
        public async Task<ApiResponse> GenerateInvoicesAsync(int month, int year, int? dueInDays, List<int>? tenantIds)
        {
            if (month < 1 || month > 12 || year < 2000 || year > 2100)
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Select a valid month and year." };

            // Billing window: the last 12 months (catching up leases that started before the company used the app, or a
            // run done late), this month and next month. Older months can't be billed by accident. A past month gets its
            // real due date, so it counts late fees only while it is unpaid (a payment recorded with its real date stops them).
            var thisMonth = new DateTime(BusinessDate.Today.Year, BusinessDate.Today.Month, 1);
            var requested = new DateTime(year, month, 1);
            if (requested < thisMonth.AddMonths(-12) || requested > thisMonth.AddMonths(1))
                return new ApiResponse
                {
                    IsSuccess = false,
                    ErrorMessage = $"Rent can only be generated for {thisMonth.AddMonths(-12):MMMM yyyy} to {thisMonth.AddMonths(1):MMMM yyyy}.",
                };
            if (dueInDays < 0 || dueInDays > 90)
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Due days must be between 0 and 90." };

            // Read once, so every invoice of this run gets the same due days and late-fee rule
            var settings = await _lateFeeSettings.GetAsync();
            int dueDays = dueInDays ?? settings.PaymentDueDays;

            var monthStart = new DateTime(year, month, 1);
            bool allTenants = tenantIds == null || tenantIds.Count == 0;

            // Leases covering the month for the selected tenants; those not invoiced yet are the ones to bill
            var covering = (await _dal.GetLeaseChargesForMonth(month, year)).AsEnumerable()
                .Where(r => allTenants || tenantIds!.Contains(Convert.ToInt32(r["TenantId"])))
                .ToList();
            var rowList = covering.Where(r => !Convert.ToBoolean(r["AlreadyInvoiced"])).ToList();

            // Selected tenants with no lease for this month can't be billed: name them instead of
            // reporting them as "already invoiced"
            string withoutLease = "";
            if (!allTenants)
            {
                var covered = covering.Select(r => Convert.ToInt32(r["TenantId"])).ToHashSet();
                var names = (await _dal.GetTenantNamesAsync()).AsEnumerable()
                    .Where(r => tenantIds!.Contains(Convert.ToInt32(r["TenantId"])) && !covered.Contains(Convert.ToInt32(r["TenantId"])))
                    .Select(r => r["Name"].ToString())
                    .ToList();
                if (names.Count > 0) withoutLease = string.Join(", ", names);
            }

            if (rowList.Count == 0)
            {
                // No lease at all is something to fix (shown as an error); "already invoiced" is the normal safe re-run
                if (covering.Count == 0)
                {
                    var noLease = allTenants
                        ? $"No lease covers {monthStart:MMMM yyyy}, so there is no rent to generate. Create a lease in Lease Management first."
                        : $"No lease covers {monthStart:MMMM yyyy} for: {withoutLease}. Create a lease in Lease Management first.";
                    return new ApiResponse { IsSuccess = false, RowsAffected = 0, Message = noLease, ErrorMessage = noLease };
                }

                return new ApiResponse
                {
                    IsSuccess = true,
                    RowsAffected = 0,
                    Message = $"Nothing to generate — every lease covering {monthStart:MMMM yyyy} for the selected tenant(s) is already invoiced."
                        + (withoutLease != "" ? $" No lease for: {withoutLease}." : ""),
                };
            }

            int created = 0;

            foreach (var row in rowList)
            {
                int tenantId = Convert.ToInt32(row["TenantId"]);
                decimal amount = Convert.ToDecimal(row["Amount"]);
                int leaseId = Convert.ToInt32(row["LeaseId"]);
                int unitId = Convert.ToInt32(row["UnitId"]);
                // The invoice starts on the first covered day (the 1st, or the move-in day), so the due date
                // is never before move-in and no late fee can apply before the tenant has moved in.
                var invoiceDate = Convert.ToDateTime(row["FromDate"]);
                var dueDate = invoiceDate.AddDays(dueDays);
                string? description = row["Descr"] == DBNull.Value ? null : row["Descr"].ToString();

                try
                {
                    var result = await _dal.CreateInvoice(tenantId, amount, invoiceDate, dueDate,
                        settings.LateFeePerDay, settings.MaxLateFeeMultiplier, description, leaseId: leaseId, unitId: unitId);
                    if (result.IsSuccess) created++;
                }
                catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627)
                {
                    // Already billed (e.g. generated at the same moment by another request): skip, never double-bill
                }
            }

            return new ApiResponse
            {
                IsSuccess = true,
                RowsAffected = created,
                Message = $"{created} invoice(s) generated for {monthStart:MMMM yyyy}."
                    + (withoutLease != "" ? $" Skipped (no lease for this month): {withoutLease}." : ""),
            };
        }

        // ---------------- ONE-TIME EXTRA CHARGES ----------------

        // Creates one standalone invoice per selected tenant for a one-off charge
        // (Maintenance, Late Fine, Security Deposit, etc.) — separate from their
        // regular monthly rent invoice, clearly labeled via Description/ChargeType.
        public static readonly string[] ExtraChargeTypes = { "Maintenance", "Utility", "Damage", "Rent Correction", "Other" };
        private static readonly string[] TypesNeedingDescription = { "Rent Correction", "Other" };
        public const decimal MaxExtraChargeAmount = 10_000_000;

        // Input checks for an extra charge (the tenant/related-invoice checks run in the database, in one transaction)
        public static string? ValidateExtraCharge(ExtraChargeRequest? req, DateTime today)
        {
            if (req == null) return "Please enter the charge.";
            if (req.TenantIds == null || req.TenantIds.Count == 0) return "Select at least one tenant.";
            if (req.TenantIds.Any(id => id <= 0)) return "One or more selected tenants are invalid.";
            if (req.TenantIds.Count > 1000) return "Select at most 1,000 tenants at a time.";
            if (!ExtraChargeTypes.Contains(req.ChargeType)) return $"Charge type must be one of: {string.Join(", ", ExtraChargeTypes)}.";
            if (req.Amount <= 0 || req.Amount > MaxExtraChargeAmount) return $"Amount must be greater than 0 and at most {MaxExtraChargeAmount:N0}.";
            if (req.Amount != decimal.Round(req.Amount, 2)) return "Amount can have at most 2 decimal places.";
            var description = req.Description?.Trim() ?? "";
            if (description.Length > 255) return "Description can be at most 255 characters.";
            if (description.Length == 0 && TypesNeedingDescription.Contains(req.ChargeType)) return $"Please describe the {req.ChargeType} charge.";
            if (req.DueInDays is < 0 or > 90) return "Due days must be between 0 and 90.";
            var date = (req.ChargeDate ?? today).Date;
            if (date < today.AddYears(-1) || date > today.AddMonths(1)) return "Charge date must be within the last 12 months or the next month.";
            if (req.RelatedInvoiceId != null)
            {
                if (req.RelatedInvoiceId <= 0) return "The related invoice is invalid.";
                if (req.TenantIds.Distinct().Count() != 1) return "A related invoice can only be set when charging one tenant.";
            }
            return null;
        }

        // Creates one separate invoice per tenant: invoice date = charge date (default today), due date = charge date +
        // due days (default the Payment Due Days setting), late fee per the current setting unless switched off.
        // All or nothing; the related invoice (if any) is never changed.
        public async Task<ApiResponse> CreateExtraChargeAsync(ExtraChargeRequest req)
        {
            var error = ValidateExtraCharge(req, BusinessDate.Today);
            if (error != null)
                return new ApiResponse { IsSuccess = false, ErrorMessage = error };

            var settings = await _lateFeeSettings.GetAsync();
            var chargeDate = (req.ChargeDate ?? BusinessDate.Today).Date;
            var dueDate = chargeDate.AddDays(req.DueInDays ?? settings.PaymentDueDays);
            var tenantIds = req.TenantIds.Distinct().ToList();
            var description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();

            var dt = await _dal.CreateExtraChargesAsync(tenantIds, chargeDate, dueDate, req.ChargeType, description, req.Amount,
                req.RelatedInvoiceId, req.ApplyLateFee ? settings.LateFeePerDay : 0, settings.MaxLateFeeMultiplier);
            var result = dt.Rows.Count > 0 ? dt.Rows[0]["Result"].ToString() : null;

            return result switch
            {
                "OK" => new ApiResponse
                {
                    IsSuccess = true,
                    RowsAffected = dt.Rows.Count,
                    Id = dt.Rows.Count == 1 ? Convert.ToInt32(dt.Rows[0]["Info"]) : null,
                    Message = dt.Rows.Count == 1
                        ? $"{req.ChargeType} charge of {req.Amount:N0} added as invoice #{dt.Rows[0]["Info"]}, due {dueDate:dd MMM yyyy}."
                        : $"{req.ChargeType} charge of {req.Amount:N0} added for {dt.Rows.Count} tenants, due {dueDate:dd MMM yyyy}.",
                },
                "BAD_TENANT" => new ApiResponse { IsSuccess = false, ErrorMessage = "One or more selected tenants don't exist. Nothing was added." },
                "BAD_RELATED" => new ApiResponse { IsSuccess = false, ErrorMessage = "The related invoice doesn't exist, is cancelled, or belongs to another tenant. Nothing was added." },
                "DUPLICATE" => new ApiResponse { IsSuccess = false, ErrorMessage = "This charge was just added. It wasn't added again." },
                "BUSY" => new ApiResponse { IsSuccess = false, ErrorMessage = "Another charge is being added right now. Please try again." },
                _ => new ApiResponse { IsSuccess = false, ErrorMessage = "The charge could not be added." },
            };
        }


        //Newly Added
        public async Task<DataTable> GetAllPaymentsAsync(DateTime? from, DateTime? to) => await _dal.GetAllPaymentsAsync(from, to);

        public async Task<ApiResponse> ChargeLateFeeAsync(int invoiceId)
        {
            var dt = await _dal.ChargeLateFeeAsync(invoiceId);
            var fee = Convert.ToDecimal(dt.Rows[0]["Fee"]);
            return fee > 0
                ? new ApiResponse { IsSuccess = true, Message = $"Late fee of {fee:N0} charged." }
                : new ApiResponse { IsSuccess = false, ErrorMessage = "Late fee is not applicable, was already charged, or the invoice is part of a move-out settlement." };
        }
    }
}