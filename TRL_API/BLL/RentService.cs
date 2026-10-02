using System.Data;
using TRL_API.DAL;
using TRL_API.Helpers;
using TRL_API.Models;

namespace TRL_API.BLL
{
    public class RentService : IRentService
    {
        private readonly IRentRepository _dal;

        public RentService(IRentRepository dal)
        {
            _dal = dal;
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

        // Records new payments; all of them share one connection and transaction, so a failure rolls every one back.
        public async Task<ApiResponse> SubmitPaymentsAsync(List<Payments> payments, int userId)
        {
            using var conn = await _dal.GetOpenConnectionAsync();
            using var transaction = conn.BeginTransaction();

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

                var updateResult = await _dal.RecalcInvoiceAsync(payment.RentInvoiceId, conn, transaction);
                if (!updateResult.IsSuccess)
                {
                    transaction.Rollback();
                    return new ApiResponse { IsSuccess = false, Message = $"Failed to update invoice #{payment.RentInvoiceId} after payment." };
                }
            }

            transaction.Commit();
            return new ApiResponse { IsSuccess = true, Message = "Payment recorded successfully." };
        }

        public async Task<DataTable> GetOccupancyAsync() => await _dal.GetOccupancyAsync();
        public async Task<DataTable> GetVacantUnitsAsync(int? includeUnitId) => await _dal.GetVacantUnitsAsync(includeUnitId);

        public async Task<ApiResponse> ReverseLateFeeAsync(int invoiceId, string reason, int userId)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Reason is required." };
            var dt = await _dal.ReverseLateFeeAsync(invoiceId, reason.Trim(), userId);
            return Convert.ToDecimal(dt.Rows[0]["Fee"]) > 0
                ? new ApiResponse { IsSuccess = true, Message = "Late fee reversed." }
                : new ApiResponse { IsSuccess = false, ErrorMessage = "Nothing to reverse (no charged fee, or payments already cover it)." };
        }

        public async Task<ApiResponse> CreateRentAsync(List<Payments> payments, int userId) =>
            await SubmitPaymentsAsync(payments, userId);

        // Edits existing payments (matched by payment Id) and recalculates each invoice, all in one transaction.
        public async Task<ApiResponse> UpdatePaymentsAsync(List<Payments> payments, int userId)
        {
            if (payments == null || payments.Count == 0)
                return new ApiResponse { IsSuccess = false, ErrorMessage = "No payments to update." };
            if (payments.Any(p => p.Id <= 0))
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Payment Id is required to update a payment." };

            using var conn = await _dal.GetOpenConnectionAsync();
            using var transaction = conn.BeginTransaction();

            foreach (var payment in payments)
            {
                var info = await _dal.GetPaymentEditInfoAsync(payment.Id, payment.RentInvoiceId, conn, transaction);
                string? err = info switch
                {
                    null => $"Payment #{payment.Id} was not found on invoice #{payment.RentInvoiceId}.",
                    { Current: < 0 } => "Adjustments (reversals) can't be edited. Record a new adjustment instead.",
                    // The edit must not leave the invoice with negative cash (e.g. below an existing reversal)
                    { Others: var others } when others + payment.PaymentAmount < 0 =>
                        $"This change would make the total paid on invoice #{payment.RentInvoiceId} negative.",
                    _ => null,
                };
                err ??= await _dal.ValidatePaymentAsync(payment, conn, transaction, payment.Id);
                if (err != null)
                {
                    transaction.Rollback();
                    return new ApiResponse { IsSuccess = false, Message = err, ErrorMessage = err };
                }

                var updateResult = await _dal.UpdatePaymentAsync(payment, userId, conn, transaction);
                if (!updateResult.IsSuccess)
                {
                    transaction.Rollback();
                    var msg = $"Payment #{payment.Id} was not found on invoice #{payment.RentInvoiceId}.";
                    return new ApiResponse { IsSuccess = false, Message = msg, ErrorMessage = msg };
                }

                var recalcResult = await _dal.RecalcInvoiceAsync(payment.RentInvoiceId, conn, transaction);
                if (!recalcResult.IsSuccess)
                {
                    transaction.Rollback();
                    return new ApiResponse { IsSuccess = false, Message = $"Failed to update invoice #{payment.RentInvoiceId} after payment." };
                }
            }

            transaction.Commit();
            return new ApiResponse { IsSuccess = true, Message = "Payment updated successfully.", RowsAffected = payments.Count };
        }

        public async Task<ApiResponse> CreatePaymentAdjustmentAsync(Payments payment, int userId)
        {
            using var conn = await _dal.GetOpenConnectionAsync();
            using var transaction = conn.BeginTransaction();

            var err = await _dal.ValidateAdjustmentAsync(payment, conn, transaction);
            if (err != null)
            {
                transaction.Rollback();
                return new ApiResponse { IsSuccess = false, ErrorMessage = err };
            }

            var insertResult = await _dal.CreatePaymentAdjustmentAsync(payment, userId, conn, transaction);
            if (!insertResult.IsSuccess)
            {
                transaction.Rollback();
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Failed to record adjustment." };
            }

            // FIX: the original adjustment path only inserted into Payments and
            // never touched RentInvoices — meaning an adjustment that fully paid
            // off an invoice would never update its StatusId, and it would keep
            // showing as Unpaid/Partial in the collections list forever.
            var recalcResult = await _dal.RecalcInvoiceAsync(payment.RentInvoiceId, conn, transaction);
            if (!recalcResult.IsSuccess)
            {
                transaction.Rollback();
                return new ApiResponse { IsSuccess = false, ErrorMessage = $"Failed to update invoice #{payment.RentInvoiceId} after adjustment." };
            }

            transaction.Commit();
            return new ApiResponse { IsSuccess = true, Message = "Adjustment recorded." };
        }

        public async Task<ApiResponse> DeletePaymentAsync(int invoiceId)
        {
            var result = await _dal.DeleteLastPaymentForInvoice(invoiceId);
            return result.IsSuccess
                ? new ApiResponse { IsSuccess = true, Message = "Last payment on this invoice was deleted." }
                : new ApiResponse { IsSuccess = false, ErrorMessage = "No payment found to delete for this invoice." };
        }

        public async Task<ApiResponse> BulkUpdateDueDateAsync(List<int> invoiceIds, DateTime newDueDate)
        {
            var result = await _dal.BulkUpdateDueDateAsync(invoiceIds, newDueDate);
            return result.IsSuccess
                ? new ApiResponse { IsSuccess = true, Message = $"Due date updated for {invoiceIds.Count} invoice(s)." }
                : new ApiResponse { IsSuccess = false, ErrorMessage = "Failed to update due dates." };
        }

        public async Task<DataTable> GetActiveTenantsAsync() => await _dal.GetActiveTenantsList();

        // ---------------- BULK INVOICE GENERATION (manual button) ----------------

        // Bills rent from the leases covering the month: one invoice per lease, prorated for partial months.
        // tenantIds: null or empty = all tenants. Otherwise only those tenants' leases.
        // Leases already billed this month are skipped, so re-running is always safe.
        public async Task<ApiResponse> GenerateInvoicesAsync(int month, int year, int dueInDays, List<int>? tenantIds)
        {
            if (month < 1 || month > 12 || year < 2000 || year > 2100)
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Select a valid month and year." };

            // Billing window: last month (for a run done late, early in the new month), this month and next month.
            // Older months are never generated, so months that were never invoiced in the past can't be billed by accident.
            var thisMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var requested = new DateTime(year, month, 1);
            if (requested < thisMonth.AddMonths(-1) || requested > thisMonth.AddMonths(1))
                return new ApiResponse
                {
                    IsSuccess = false,
                    ErrorMessage = $"Rent can only be generated for {thisMonth.AddMonths(-1):MMMM yyyy}, {thisMonth:MMMM yyyy} or {thisMonth.AddMonths(1):MMMM yyyy}.",
                };
            if (dueInDays < 0 || dueInDays > 90)
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Due days must be between 0 and 90." };

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
                var dueDate = invoiceDate.AddDays(dueInDays);
                string? description = row["Descr"] == DBNull.Value ? null : row["Descr"].ToString();

                try
                {
                    var result = await _dal.CreateInvoice(tenantId, amount, invoiceDate, dueDate, description, leaseId: leaseId, unitId: unitId);
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
        public async Task<ApiResponse> CreateExtraChargeAsync(
            List<int> tenantIds, int month, int year, string chargeType, string description, decimal amount, int dueInDays)
        {
            if (tenantIds == null || tenantIds.Count == 0)
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Select at least one tenant." };

            if (amount <= 0)
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Amount must be greater than zero." };
            if (month < 1 || month > 12 || year < 2000 || year > 2100)
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Select a valid month and year." };
            if (dueInDays < 0 || dueInDays > 90)
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Due days must be between 0 and 90." };
            if (string.IsNullOrWhiteSpace(chargeType))
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Charge type is required." };

            var invoiceDate = new DateTime(year, month, 1);
            var dueDate = invoiceDate.AddDays(dueInDays);
            int created = 0;

            foreach (var tenantId in tenantIds)
            {
                var result = await _dal.CreateInvoice(tenantId, amount, invoiceDate, dueDate, description, chargeType);
                if (result.IsSuccess) created++;
            }

            return new ApiResponse
            {
                IsSuccess = true,
                RowsAffected = created,
                Message = $"{chargeType} charge of {amount:C} added for {created} tenant(s).",
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
                : new ApiResponse { IsSuccess = false, ErrorMessage = "Late fee is not applicable or was already charged." };
        }
    }
}