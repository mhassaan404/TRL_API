using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.DAL;
using TRL_API.Data;
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

        public async Task<DataTable> GetInvoiceDetailsAsync(int invoiceId)
        {
            return await _dal.GetInvoiceDetailsAsync(invoiceId);
        }

        // Payments
        public async Task<ApiResponse> CreateRentAsync(Payments payment)
        {
            try
            {
                int result = await _dbHelper.ExecuteTransactionAsync(async (conn, transaction) =>
                {
                    int rows1 = await _dal.CreateRentAsync(payment, conn, transaction);
                    if (rows1 == 0) throw new Exception("Failed to add payment.");

                    int rows2 = await _dal.UpdateInvoiceAfterRentAsync(payment.RentInvoiceId, payment.PaymentAmount, conn, transaction);
                    if (rows2 == 0) throw new Exception("Failed to update invoice.");

                    return 1; // success
                });

                return new ApiResponse { IsSuccess = true, Message = "Payment added and invoice updated successfully." };
            }
            catch (SqlException ex)
            {
                // Handle foreign key or other SQL errors specifically if needed
                return new ApiResponse { IsSuccess = false, ErrorMessage = $"Database error: {ex.Message}" };
            }
            catch (Exception ex)
            {
                return new ApiResponse { IsSuccess = false, ErrorMessage = $"Error: {ex.Message}" };
            }
        }

        public async Task<DataTable> GetRentCollectionAsync()
        {
            return await _dal.GetRentCollectionAsync();
        }

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
