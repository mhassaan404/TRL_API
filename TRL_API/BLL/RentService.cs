//using System.Data;
//using TRL_API.DAL;
//using TRL_API.Helpers;
//using TRL_API.Models;

//namespace TRL_API.BLL
//{
//    public class RentService
//    {
//        private readonly RentRepository _dal;

//        public RentService(RentRepository dal)
//        {
//            _dal = dal;
//        }

//        public async Task<DataTable> GetTenantsAsync() => await _dal.GetTenantsAsync();

//        public async Task<DataTable> GetStatusListAsync() => await _dal.GetStatusListAsync();

//        public async Task<DataTable> GetInvoicesByTenantAsync(int tenantId) => await _dal.GetInvoicesByTenantAsync(tenantId);

//        public async Task<object> GetInvoiceByIdAsync(int invoiceId)
//        {
//            var dt = await _dal.GetInvoiceByIdAsync(invoiceId);
//            var rows = DataTableHelper.ToDictionaryList(dt, true);
//            return new { invoiceId, payments = rows };
//        }

//        public async Task<DataTable> GetPaymentHistoryByIdAsync(int invoiceId) => await _dal.GetPaymentHistoryByIdAsync(invoiceId);

//        public async Task<DataTable> GetRentCollectionAsync() => await _dal.GetRentCollectionAsync();

//        public async Task<DataTable> GetPaymentHistoryAsync(int invoiceId) => await _dal.GetPaymentHistoryAsync(invoiceId);

//        // ---------------- UNPAID INVOICES + SUMMARY FOR A TENANT ----------------

//        public async Task<object> GetUnpaidInvoicesByTenantAsync(int tenantId)
//        {
//            var dt = await _dal.GetUnpaidInvoiceByTenant(tenantId);
//            var invoices = DataTableHelper.ToDictionaryList(dt, true);

//            decimal monthlyRent = 0, pending = 0, previousBalance = 0, totalLateFee = 0;

//            if (dt.Rows.Count > 0)
//            {
//                // Query orders by InvoiceDate DESC, so row 0 is the most recent invoice.
//                monthlyRent = Convert.ToDecimal(dt.Rows[0]["MonthlyRent"]);
//                totalLateFee = Convert.ToDecimal(dt.Rows[0]["TotalLateFeePerTenant"]);

//                for (int i = 0; i < dt.Rows.Count; i++)
//                {
//                    var remaining = Convert.ToDecimal(dt.Rows[i]["RemainingAmount"]);
//                    pending += remaining;

//                    // "Previous balance" = arrears from every invoice except the
//                    // most recent one (row 0).
//                    if (i > 0)
//                    {
//                        previousBalance += remaining;
//                    }
//                }
//            }

//            var summary = new
//            {
//                monthlyRent,
//                pending,
//                previousBalance,
//                totalLateFee,
//            };

//            return new { invoices, summary };
//        }

//        // ---------------- PAYMENTS ----------------

//        // TODO: adjust this if your DbHelper exposes the connection differently
//        // (e.g. a different method name). This just needs an open SqlConnection
//        // so multiple payments in one submission share a single transaction.
//        public async Task<ApiResponse> SubmitOrUpdatePayments(List<Payments> payments, int userId)
//        {
//            using var conn = await _dal.GetOpenConnectionAsync();
//            using var transaction = conn.BeginTransaction();

//            try
//            {
//                foreach (var payment in payments)
//                {
//                    var insertResult = await _dal.CreateRentAsync(payment, userId, conn, transaction);
//                    if (!insertResult.IsSuccess)
//                    {
//                        transaction.Rollback();
//                        return new ApiResponse { IsSuccess = false, Message = $"Failed to record payment for invoice #{payment.RentInvoiceId}." };
//                    }

//                    var updateResult = await _dal.UpdateInvoiceAfterRentAsync(payment.RentInvoiceId, payment.PaymentAmount + payment.DiscountAmount, conn, transaction);
//                    if (!updateResult.IsSuccess)
//                    {
//                        transaction.Rollback();
//                        return new ApiResponse { IsSuccess = false, Message = $"Failed to update invoice #{payment.RentInvoiceId} after payment." };
//                    }
//                }

//                transaction.Commit();
//                return new ApiResponse { IsSuccess = true, Message = "Payment recorded successfully." };
//            }
//            catch (Exception ex)
//            {
//                transaction.Rollback();
//                return new ApiResponse { IsSuccess = false, Message = "Error occurred while recording payment. " + ex.Message };
//            }
//        }

//        public async Task<ApiResponse> CreateRentAsync(List<Payments> payments, int userId) =>
//            await SubmitOrUpdatePayments(payments, userId);

//        public async Task<ApiResponse> UpdatePaymentsAsync(List<Payments> payments, int userId) =>
//            await SubmitOrUpdatePayments(payments, userId);

//        public async Task<ApiResponse> CreatePaymentAdjustmentAsync(Payments payment, int userId)
//        {
//            try
//            {
//                var insertResult = await _dal.CreatePaymentAdjustmentAsync(payment, userId);
//                if (!insertResult.IsSuccess)
//                {
//                    return new ApiResponse { IsSuccess = false, ErrorMessage = "Failed to record adjustment." };
//                }

//                // FIX: the original adjustment path only inserted into Payments and
//                // never touched RentInvoices — meaning an adjustment that fully paid
//                // off an invoice would never update its StatusId, and it would keep
//                // showing as Unpaid/Partial in the collections list forever.
//                await _dal.UpdateInvoiceAfterRentAsync(
//                    payment.RentInvoiceId,
//                    payment.PaymentAmount + payment.DiscountAmount);

//                return new ApiResponse { IsSuccess = true, Message = "Adjustment recorded." };
//            }
//            catch (Exception ex)
//            {
//                return new ApiResponse { IsSuccess = false, ErrorMessage = "Error occurred while recording adjustment. " + ex.Message };
//            }
//        }

//        public async Task<ApiResponse> DeletePaymentAsync(int invoiceId)
//        {
//            try
//            {
//                var result = await _dal.DeleteLastPaymentForInvoice(invoiceId);
//                return result.IsSuccess
//                    ? new ApiResponse { IsSuccess = true, Message = "Last payment on this invoice was deleted." }
//                    : new ApiResponse { IsSuccess = false, ErrorMessage = "No payment found to delete for this invoice." };
//            }
//            catch (Exception ex)
//            {
//                return new ApiResponse { IsSuccess = false, ErrorMessage = "Error occurred while deleting payment. " + ex.Message };
//            }
//        }

//        public async Task<ApiResponse> BulkUpdateDueDateAsync(List<int> invoiceIds, DateTime newDueDate)
//        {
//            try
//            {
//                var result = await _dal.BulkUpdateDueDateAsync(invoiceIds, newDueDate);
//                return result.IsSuccess
//                    ? new ApiResponse { IsSuccess = true, Message = $"Due date updated for {invoiceIds.Count} invoice(s)." }
//                    : new ApiResponse { IsSuccess = false, ErrorMessage = "Failed to update due dates." };
//            }
//            catch (Exception ex)
//            {
//                return new ApiResponse { IsSuccess = false, ErrorMessage = "Error occurred while updating due dates. " + ex.Message };
//            }
//        }

//        // ---------------- BULK INVOICE GENERATION (manual button) ----------------

//        public async Task<ApiResponse> GenerateInvoicesAsync(int month, int year, int dueInDays)
//        {
//            try
//            {
//                var tenants = await _dal.GetActiveTenantsWithoutInvoiceForMonth(month, year);

//                if (tenants.Rows.Count == 0)
//                {
//                    return new ApiResponse
//                    {
//                        IsSuccess = true,
//                        RowsAffected = 0,
//                        Message = "All active tenants already have an invoice for this month.",
//                    };
//                }

//                var invoiceDate = new DateTime(year, month, 1);
//                var dueDate = invoiceDate.AddDays(dueInDays);
//                int created = 0;

//                foreach (DataRow row in tenants.Rows)
//                {
//                    int tenantId = Convert.ToInt32(row["TenantId"]);
//                    decimal monthlyRent = Convert.ToDecimal(row["MonthlyRent"]);

//                    var result = await _dal.CreateInvoice(tenantId, monthlyRent, invoiceDate, dueDate);
//                    if (result.IsSuccess) created++;
//                }

//                return new ApiResponse
//                {
//                    IsSuccess = true,
//                    RowsAffected = created,
//                    Message = $"{created} invoice(s) generated for {invoiceDate:MMMM yyyy}.",
//                };
//            }
//            catch (Exception ex)
//            {
//                return new ApiResponse { IsSuccess = false, Message = "Error occurred while generating invoices. " + ex.Message };
//            }
//        }
//    }
//}




using System.Data;
using TRL_API.DAL;
using TRL_API.Helpers;
using TRL_API.Models;

namespace TRL_API.BLL
{
    public class RentService
    {
        private readonly RentRepository _dal;

        public RentService(RentRepository dal)
        {
            _dal = dal;
        }

        public async Task<DataTable> GetTenantsAsync() => await _dal.GetTenantsAsync();

        public async Task<DataTable> GetStatusListAsync() => await _dal.GetStatusListAsync();

        public async Task<DataTable> GetInvoicesByTenantAsync(int tenantId) => await _dal.GetInvoicesByTenantAsync(tenantId);

        public async Task<object> GetInvoiceByIdAsync(int invoiceId)
        {
            var dt = await _dal.GetInvoiceByIdAsync(invoiceId);
            var rows = DataTableHelper.ToDictionaryList(dt, true);
            return new { invoiceId, payments = rows };
        }

        public async Task<DataTable> GetPaymentHistoryByIdAsync(int invoiceId) => await _dal.GetPaymentHistoryByIdAsync(invoiceId);

        public async Task<DataTable> GetRentCollectionAsync() => await _dal.GetRentCollectionAsync();

        public async Task<DataTable> GetTenantsWithRentAsync() => await _dal.GetTenantsWithRent();

        public async Task<ApiResponse> UpdateTenantMonthlyRentAsync(int tenantId, decimal newRent)
        {
            if (newRent <= 0) return new ApiResponse { IsSuccess = false, ErrorMessage = "Rent must be greater than zero." };
            return await _dal.UpdateTenantMonthlyRent(tenantId, newRent);
        }

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

        // TODO: adjust this if your DbHelper exposes the connection differently
        // (e.g. a different method name). This just needs an open SqlConnection
        // so multiple payments in one submission share a single transaction.
        public async Task<ApiResponse> SubmitOrUpdatePayments(List<Payments> payments, int userId)
        {
            using var conn = await _dal.GetOpenConnectionAsync();
            using var transaction = conn.BeginTransaction();

            try
            {
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

                    var updateResult = await _dal.UpdateInvoiceAfterRentAsync(payment.RentInvoiceId, payment.PaymentAmount + payment.DiscountAmount, conn, transaction);
                    if (!updateResult.IsSuccess)
                    {
                        transaction.Rollback();
                        return new ApiResponse { IsSuccess = false, Message = $"Failed to update invoice #{payment.RentInvoiceId} after payment." };
                    }
                }

                transaction.Commit();
                return new ApiResponse { IsSuccess = true, Message = "Payment recorded successfully." };
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return new ApiResponse { IsSuccess = false, Message = "Error occurred while recording payment. " + ex.Message };
            }
        }

        public async Task<DataTable> GetOccupancyAsync() => await _dal.GetOccupancyAsync();
        public async Task<DataTable> GetVacantUnitsAsync(int? includeUnitId) => await _dal.GetVacantUnitsAsync(includeUnitId);
        public async Task<decimal> GetUnitRentAsync(int unitId) => await _dal.GetUnitRentAsync(unitId);

        public async Task<ApiResponse> ReverseLateFeeAsync(int invoiceId, string reason, int userId)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Reason is required." };
            try
            {
                var dt = await _dal.ReverseLateFeeAsync(invoiceId, reason.Trim(), userId);
                return Convert.ToDecimal(dt.Rows[0]["Fee"]) > 0
                    ? new ApiResponse { IsSuccess = true, Message = "Late fee reversed." }
                    : new ApiResponse { IsSuccess = false, ErrorMessage = "Nothing to reverse (no charged fee, or payments already cover it)." };
            }
            catch (Exception ex)
            {
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Error reversing late fee. " + ex.Message };
            }
        }

        public async Task<ApiResponse> CreateRentAsync(List<Payments> payments, int userId) =>
            await SubmitOrUpdatePayments(payments, userId);

        public async Task<ApiResponse> UpdatePaymentsAsync(List<Payments> payments, int userId) =>
            await SubmitOrUpdatePayments(payments, userId);

        public async Task<ApiResponse> CreatePaymentAdjustmentAsync(Payments payment, int userId)
        {
            try
            {
                var insertResult = await _dal.CreatePaymentAdjustmentAsync(payment, userId);
                if (!insertResult.IsSuccess)
                {
                    return new ApiResponse { IsSuccess = false, ErrorMessage = "Failed to record adjustment." };
                }

                // FIX: the original adjustment path only inserted into Payments and
                // never touched RentInvoices — meaning an adjustment that fully paid
                // off an invoice would never update its StatusId, and it would keep
                // showing as Unpaid/Partial in the collections list forever.
                await _dal.UpdateInvoiceAfterRentAsync(
                    payment.RentInvoiceId,
                    payment.PaymentAmount + payment.DiscountAmount);

                return new ApiResponse { IsSuccess = true, Message = "Adjustment recorded." };
            }
            catch (Exception ex)
            {
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Error occurred while recording adjustment. " + ex.Message };
            }
        }

        public async Task<ApiResponse> DeletePaymentAsync(int invoiceId)
        {
            try
            {
                var result = await _dal.DeleteLastPaymentForInvoice(invoiceId);
                return result.IsSuccess
                    ? new ApiResponse { IsSuccess = true, Message = "Last payment on this invoice was deleted." }
                    : new ApiResponse { IsSuccess = false, ErrorMessage = "No payment found to delete for this invoice." };
            }
            catch (Exception ex)
            {
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Error occurred while deleting payment. " + ex.Message };
            }
        }

        public async Task<ApiResponse> BulkUpdateDueDateAsync(List<int> invoiceIds, DateTime newDueDate)
        {
            try
            {
                var result = await _dal.BulkUpdateDueDateAsync(invoiceIds, newDueDate);
                return result.IsSuccess
                    ? new ApiResponse { IsSuccess = true, Message = $"Due date updated for {invoiceIds.Count} invoice(s)." }
                    : new ApiResponse { IsSuccess = false, ErrorMessage = "Failed to update due dates." };
            }
            catch (Exception ex)
            {
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Error occurred while updating due dates. " + ex.Message };
            }
        }

        public async Task<DataTable> GetActiveTenantsAsync() => await _dal.GetActiveTenantsList();

        // ---------------- BULK INVOICE GENERATION (manual button) ----------------

        // tenantIds: null or empty = all active tenants without an invoice yet
        // this month. Otherwise, only the specified tenants (still skips anyone
        // among them who already has one, so re-running is always safe).
        public async Task<ApiResponse> GenerateInvoicesAsync(int month, int year, int dueInDays, List<int>? tenantIds)
        {
            try
            {
                var eligible = await _dal.GetActiveTenantsWithoutInvoiceForMonth(month, year);

                var rowsToGenerate = (tenantIds == null || tenantIds.Count == 0)
                    ? eligible.AsEnumerable()
                    : eligible.AsEnumerable().Where(r => tenantIds.Contains(Convert.ToInt32(r["TenantId"])));

                var rowList = rowsToGenerate.ToList();

                if (rowList.Count == 0)
                {
                    return new ApiResponse
                    {
                        IsSuccess = true,
                        RowsAffected = 0,
                        Message = "No invoices to generate — selected tenant(s) already have one for this month.",
                    };
                }

                var invoiceDate = new DateTime(year, month, 1);
                var dueDate = invoiceDate.AddDays(dueInDays);
                int created = 0;

                foreach (var row in rowList)
                {
                    int tenantId = Convert.ToInt32(row["TenantId"]);
                    decimal monthlyRent = Convert.ToDecimal(row["MonthlyRent"]);

                    var result = await _dal.CreateInvoice(tenantId, monthlyRent, invoiceDate, dueDate);
                    if (result.IsSuccess) created++;
                }

                return new ApiResponse
                {
                    IsSuccess = true,
                    RowsAffected = created,
                    Message = $"{created} invoice(s) generated for {invoiceDate:MMMM yyyy}.",
                };
            }
            catch (Exception ex)
            {
                return new ApiResponse { IsSuccess = false, Message = "Error occurred while generating invoices. " + ex.Message };
            }
        }

        // ---------------- ONE-TIME EXTRA CHARGES ----------------

        // Creates one standalone invoice per selected tenant for a one-off charge
        // (Maintenance, Late Fine, Security Deposit, etc.) — separate from their
        // regular monthly rent invoice, clearly labeled via Description/ChargeType.
        public async Task<ApiResponse> CreateExtraChargeAsync(
            List<int> tenantIds, int month, int year, string chargeType, string description, decimal amount, int dueInDays)
        {
            try
            {
                if (tenantIds == null || tenantIds.Count == 0)
                    return new ApiResponse { IsSuccess = false, ErrorMessage = "Select at least one tenant." };

                if (amount <= 0)
                    return new ApiResponse { IsSuccess = false, ErrorMessage = "Amount must be greater than zero." };

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
            catch (Exception ex)
            {
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Error occurred while adding the charge. " + ex.Message };
            }
        }


        //Newly Added
        public async Task<DataTable> GetAllPaymentsAsync(DateTime? from, DateTime? to) => await _dal.GetAllPaymentsAsync(from, to);

        public async Task<ApiResponse> ChargeLateFeeAsync(int invoiceId)
        {
            try
            {
                var dt = await _dal.ChargeLateFeeAsync(invoiceId);
                var fee = Convert.ToDecimal(dt.Rows[0]["Fee"]);
                return fee > 0
                    ? new ApiResponse { IsSuccess = true, Message = $"Late fee of {fee:N0} charged." }
                    : new ApiResponse { IsSuccess = false, ErrorMessage = "Late fee is not applicable or was already charged." };
            }
            catch (Exception ex)
            {
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Error charging late fee. " + ex.Message };
            }
        }
    }
}