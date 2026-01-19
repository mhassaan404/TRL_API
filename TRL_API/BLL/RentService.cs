using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.DAL;
using TRL_API.Data;
using TRL_API.DTOs.Rent;
using TRL_API.Helpers;
using TRL_API.Models;

namespace TRL_API.BLL
{
    public class RentService
    {
        private readonly DbHelper _dbHelper;
        private readonly RentRepository _dal;

        public RentService(RentRepository dal, DbHelper dbHelper)
        {
            _dal = dal;
            _dbHelper = dbHelper;
        }

        public async Task<DataTable> GetTenantsAsync()
        {
            return await _dal.GetTenantsAsync();
        }

        public async Task<DataTable> GetStatusListAsync()
        {
            return await _dal.GetStatusListAsync();
        }

        // Invoices
        public async Task<DataTable> GetInvoicesByTenantAsync(int tenantId)
        {
            return await _dal.GetInvoicesByTenantAsync(tenantId);
        }

        // Invoices
        //public async Task<DataTable> GetInvoiceByIdAsync(int invoiceId)
        //{
        //    DataTable dt;
        //    dt = await _dal.GetInvoiceByIdAsync(invoiceId);
        //    // Assume dt already has invoice row(s)
        //    if (dt.Rows.Count > 0)
        //    {
        //        // Add LateFee column if it doesn't exist
        //        if (!dt.Columns.Contains("LateFee"))
        //            dt.Columns.Add("LateFee", typeof(decimal));

        //        // Loop through all rows (in case multiple rows exist for the same invoice)
        //        foreach (DataRow row in dt.Rows)
        //        {
        //            decimal monthlyRent = Convert.ToDecimal(row["MonthlyRent"]);
        //            DateTime dueDate = Convert.ToDateTime(row["DueDate"]);
        //            int tenantId = Convert.ToInt32(row["TenantId"]);

        //            // Calculate late fee using your calculator
        //            decimal lateFee = LateFeeCalculator.Calculate(0, monthlyRent, dueDate, DateTime.UtcNow);

        //            // Update DataTable row
        //            row["LateFee"] = lateFee;
        //        }
        //    }
        //    return dt;
        //}
        public async Task<InvoiceResponseDto> GetInvoiceByIdAsync(int invoiceId)
        {
            DataTable dt = await _dal.GetInvoiceByIdAsync(invoiceId);

            var invoices = new List<InvoiceRowDto>();

            foreach (DataRow row in dt.Rows)
            {
                decimal monthlyRent = Convert.ToDecimal(row["MonthlyRent"]);
                decimal paid = Convert.ToDecimal(row["PaidAmount"] ?? 0);
                DateTime dueDate = Convert.ToDateTime(row["DueDate"]);

                decimal lateFee = LateFeeCalculator.Calculate(
                    0,
                    monthlyRent,
                    dueDate,
                    DateTime.UtcNow
                );

                var invoice = new InvoiceRowDto
                {
                    InvoiceId = row.GetInt("InvoiceId"),
                    PaymentId = row.GetNullableInt("PaymentId"),
                    TenantId = row.GetInt("TenantId"),
                    TenantName = row.GetString("TenantName"),
                    BuildingName = row.GetString("BuildingName"),
                    FloorNumber = row.GetString("FloorNumber"),
                    UnitNumber = row.GetString("UnitNumber"),

                    InvoiceDate = row.GetDateTime("InvoiceDate"),
                    MonthlyRent = row.GetDecimal("MonthlyRent"),
                    DueDate = row.GetDateTime("DueDate"),
                    RemainingAmount = row.GetDecimal("RemainingAmount"),
                    StatusId = row.GetInt("StatusId"),
                    StatusName = row.GetString("StatusName"),

                    PaidAmount = row.GetDecimal("PaidAmount"),
                    PaymentDate = row.GetNullableDateTime("PaymentDate"),
                    PaymentMethod = row.GetString("PaymentMethod"),
                    Notes = row.GetString("Notes"),
                    CreatedAt = row.GetDateTime("CreatedAt"),

                    DiscountAmount = row.GetDecimal("DiscountAmount"),
                    DiscountPercent = row.GetDecimal("DiscountPercent"),
                    IsLateFeeWaived = row.GetBool("IsLateFeeWaived"),

                    // LateFee calculated separately
                    LateFee = LateFeeCalculator.Calculate(0, row.GetDecimal("MonthlyRent"), row.GetDateTime("DueDate"), DateTime.UtcNow)
                };

                invoices.Add(invoice);
            }

            var summary = new InvoiceSummaryDto
            {
                MonthlyRent = invoices.LastOrDefault()?.MonthlyRent ?? 0,
                Pending = invoices.Sum(i => i.MonthlyRent - i.PaidAmount - i.DiscountAmount),
                PreviousBalance = invoices
                    .OrderBy(i => i.DueDate)
                    .SkipLast(1)
                    .Sum(i => i.MonthlyRent - i.PaidAmount - i.DiscountAmount),
                TotalLateFee = invoices.Sum(i => i.IsLateFeeWaived ? 0 : i.LateFee)
            };

            return new InvoiceResponseDto
            {
                Invoices = invoices,
                Summary = summary
            };
        }

        public async Task<DataTable> GetPaymentHistoryByIdAsync(int invoiceId)
        {
            return await _dal.GetPaymentHistoryByIdAsync(invoiceId);
        }

        public async Task<InvoiceResponseDto> GetUnpaidInvoicesByTenantAsync(int tenantId)
        {
            DataTable dt = await _dal.GetUnpaidInvoiceByTenant(tenantId);
            var invoices = new List<InvoiceRowDto>();

            foreach (DataRow row in dt.Rows)
            {
                var invoice = new InvoiceRowDto
                {
                    InvoiceId = row.GetInt("InvoiceId"),
                    PaymentId = null, // ADD mode → no payment yet

                    TenantId = row.GetInt("TenantId"),
                    TenantName = row.GetString("TenantName"),

                    InvoiceDate = row.GetDateTime("InvoiceDate"),
                    DueDate = row.GetDateTime("DueDate"),

                    MonthlyRent = row.GetDecimal("MonthlyRent"),
                    PaidAmount = row.GetDecimal("PaidAmount"),
                    RemainingAmount = row.GetDecimal("RemainingAmount"),

                    DiscountAmount = row.GetDecimal("DiscountAmount"),
                    DiscountPercent = row.GetDecimal("DiscountPercent"),

                    IsLateFeeWaived = row.GetBool("WaveLate"),

                    LateFee = row.GetDecimal("LateFee"),

                    PayAmount = 0,
                    PaymentDate = null,
                    PaymentMethod = "",
                    Notes = "",

                    StatusId = 0,
                    StatusName = ""
                };

                invoices.Add(invoice);
            }

            var summary = new InvoiceSummaryDto
            {
                MonthlyRent = invoices.LastOrDefault()?.MonthlyRent ?? 0,
                Pending = invoices.Sum(i => i.MonthlyRent - i.PaidAmount - i.DiscountAmount),
                PreviousBalance = invoices
                    .OrderBy(i => i.DueDate)
                    .SkipLast(1)
                    .Sum(i => i.MonthlyRent - i.PaidAmount - i.DiscountAmount),
                TotalLateFee = invoices.Sum(i => i.IsLateFeeWaived ? 0 : i.LateFee)
            };

            return new InvoiceResponseDto
            {
                Invoices = invoices,
                Summary = summary
            };
        }


        //public async Task<DataTable> GetUnpaidInvoiceByTenant(int tenantId)
        //{
        //    return await _dal.GetUnpaidInvoiceByTenant(tenantId);
        //}

        // Payments
        //public async Task<ApiResponse> CreateRentAsync(List<Payments> payment)
        //{
        //    try
        //    {
        //        int result = await _dbHelper.ExecuteTransactionAsync(async (conn, transaction) =>
        //        {
        //            int rows1 = await _dal.CreateRentAsync(payment, conn, transaction);
        //            if (rows1 == 0) throw new Exception("Failed to add payment.");

        //            int rows2 = await _dal.UpdateInvoiceAfterRentAsync(payment.RentInvoiceId, payment.PaymentAmount, conn, transaction);
        //            if (rows2 == 0) throw new Exception("Failed to update invoice.");

        //            return 1; // success
        //        });

        //        return new ApiResponse { IsSuccess = true, Message = "Payment added and invoice updated successfully." };
        //    }
        //    catch (SqlException ex)
        //    {
        //        // Handle foreign key or other SQL errors specifically if needed
        //        return new ApiResponse { IsSuccess = false, ErrorMessage = $"Database error: {ex.Message}" };
        //    }
        //    catch (Exception ex)
        //    {
        //        return new ApiResponse { IsSuccess = false, ErrorMessage = $"Error: {ex.Message}" };
        //    }
        //}

        public async Task<ApiResponse> CreateRentAsync(List<Payments> payments, int userId)
        {
            try
            {
                await _dbHelper.ExecuteTransactionAsync(async (conn, transaction) =>
                {
                    foreach (var payment in payments)
                    {
                        int rows1 = await _dal.CreateRentAsync(payment, userId, conn, transaction);
                        if (rows1 == 0)
                            throw new Exception("Failed to insert payment.");

                        decimal effectivePayment =
                            payment.PaymentAmount + payment.DiscountAmount;

                        int rows2 =
                            await _dal.UpdateInvoiceAfterRentAsync(payment.RentInvoiceId, effectivePayment, conn, transaction);

                        if (rows2 == 0)
                            throw new Exception("Failed to update invoice.");
                    }

                    return 1;
                });

                return new ApiResponse
                {
                    IsSuccess = true,
                    Message = "Payments processed successfully."
                };
            }
            catch (Exception ex)
            {
                return new ApiResponse
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<ApiResponse> CreatePaymentAdjustmentAsync(Payments payments, int userId)
        {
            try
            {
                int result = await _dal.CreatePaymentAdjustmentAsync(payments, userId);

                if (result > 0)
                    return new ApiResponse { IsSuccess = true, Message = "Payment adjusted successfully." };

                return new ApiResponse { IsSuccess = false, ErrorMessage = "Failed to save adjustment. Please try again." };
            }
            catch (Exception)
            {
                return new ApiResponse
                {
                    IsSuccess = false,
                    ErrorMessage = "Error occurred while saving payment adjustment."
                };
            }
        }

        public async Task<DataTable> GetRentCollectionAsync()
        {
            return await _dal.GetRentCollectionAsync();
        }

        //public async Task<InvoiceResponseDto> GetRentCollectionAsync()
        //{
        //    DataTable dt = await _dal.GetRentCollectionAsync();

        //    var invoices = new List<InvoiceRowDto>();

        //    foreach (DataRow row in dt.Rows)
        //    {
        //        // Core amounts
        //        decimal monthlyRent = Convert.ToDecimal(row["MonthlyRent"]);
        //        decimal paidAmount = Convert.ToDecimal(row["PaidAmount"] ?? 0);
        //        decimal appliedDiscount = Convert.ToDecimal(row["AppliedDiscount"] ?? 0);
        //        decimal remainingAmount = Convert.ToDecimal(row["RemainingAmount"] ?? 0);
        //        DateTime dueDate = Convert.ToDateTime(row["DueDate"]);

        //        // Calculate late fee only if there is remaining amount and past due date
        //        decimal lateFee = 0;
        //        if (remainingAmount > 0 && DateTime.UtcNow.Date > dueDate.Date)
        //        {
        //            // Optionally include any logic for waived late fee
        //            bool isLateFeeWaived = row.Table.Columns.Contains("IsLateFeeWaived") &&
        //                                   Convert.ToBoolean(row["IsLateFeeWaived"]);
        //            if (!isLateFeeWaived)
        //            {
        //                lateFee = LateFeeCalculator.Calculate(remainingAmount, monthlyRent, dueDate, DateTime.UtcNow);
        //            }
        //        }

        //        // Map invoice row
        //        var invoice = new InvoiceRowDto
        //        {
        //            InvoiceId = row.GetInt("InvoiceId"),
        //            TenantId = row.GetInt("TenantId"),
        //            TenantName = row.GetString("TenantName"),
        //            BuildingName = row.GetString("BuildingName"),
        //            FloorNumber = row.GetString("FloorNumber"),
        //            UnitNumber = row.GetString("UnitNumber"),

        //            InvoiceDate = row.GetDateTime("InvoiceDate"),
        //            MonthlyRent = monthlyRent,
        //            DueDate = dueDate,
        //            RemainingAmount = remainingAmount,
        //            StatusName = row.GetString("StatusName"),

        //            PaidAmount = paidAmount,
        //            AppliedDiscount = appliedDiscount,
        //            PaymentDate = row.GetNullableDateTime("LastPaymentDate"),

        //            LateFee = lateFee
        //        };

        //        invoices.Add(invoice);
        //    }

        //    return new InvoiceResponseDto
        //    {
        //        Invoices = invoices
        //    };
        //}

        public async Task<DataTable> GetPaymentHistoryAsync(int invoiceId)
        {
            return await _dal.GetPaymentHistoryAsync(invoiceId);
        }

        // Bulk operations
        public async Task<ApiResponse> BulkUpdateDueDateAsync(List<int> invoiceIds, DateTime newDueDate)
        {
            if (invoiceIds == null || invoiceIds.Count == 0)
                return new ApiResponse { IsSuccess = false, ErrorMessage = "Invoice list cannot be empty." };

            try
            {
                int rows = await _dal.BulkUpdateDueDateAsync(invoiceIds, newDueDate);
                if (rows > 0)
                    return new ApiResponse { IsSuccess = true, Message = "Due dates updated successfully." };

                return new ApiResponse { IsSuccess = false, ErrorMessage = "No invoices were updated." };
            }
            catch (SqlException ex)
            {
                return new ApiResponse { IsSuccess = false, ErrorMessage = $"Database error: {ex.Message}" };
            }
            catch (Exception ex)
            {
                return new ApiResponse { IsSuccess = false, ErrorMessage = $"Error: {ex.Message}" };
            }
        }
    }
}
