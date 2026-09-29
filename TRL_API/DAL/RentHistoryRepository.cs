using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.Data;
using TRL_API.Models;

namespace TRL_API.DAL
{
    public class RentHistoryRepository : IRentHistoryRepository
    {
        private readonly DbHelper _dbHelper;

        public RentHistoryRepository(DbHelper dbHelper)
        {
            _dbHelper = dbHelper;
        }


        public async Task<DataTable> GetHistoryAsync()
        {
            string query = @"
                SELECT ri.Id AS invoiceId, ri.InvoiceDate, t.Name AS Tenant, u.UnitNumber AS Unit,
                       ri.TotalRent AS MonthlyRent, ri.LateFeeCharged, ri.ChargeType,
                       bal.Paid AS PaidAmount, bal.Disc AS DiscountAmount,
                       CASE WHEN ri.StatusId = 6 THEN 0 ELSE bal.Balance END AS Balance,
                       bal.LastPaymentDate,
                       COALESCE(pt.Methods, 'N/A') AS PaymentMethod,
                       COALESCE(sl.StatusName, 'Unknown') AS Status
                FROM RentInvoices ri
                INNER JOIN Tenants t ON ri.TenantId = t.TenantId
                LEFT JOIN Units u ON u.UnitId = ISNULL(ri.UnitId, t.UnitId)
                LEFT JOIN StatusList sl ON ri.StatusId = sl.StatusId
                CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) bal
                OUTER APPLY (
                    SELECT STUFF((SELECT DISTINCT ', ' + p2.PaymentMethod FROM Payments p2
                                  WHERE p2.RentInvoiceId = ri.Id AND p2.PaymentMethod IS NOT NULL
                                  FOR XML PATH('')), 1, 2, '') AS Methods
                ) pt
                ORDER BY ri.InvoiceDate DESC, ri.Id DESC;";
            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query);
        }

        // Only allowed when the invoice has no payment records at all.
        public async Task<DataTable> CancelInvoice(int invoiceId, string reason, int userId)
        {
            string query = @"
                DECLARE @r VARCHAR(20);
                IF NOT EXISTS (SELECT 1 FROM RentInvoices WHERE Id = @InvoiceId AND StatusId <> 6)
                    SET @r = 'NOT_FOUND';
                ELSE IF EXISTS (SELECT 1 FROM Payments WHERE RentInvoiceId = @InvoiceId)
                    SET @r = 'HAS_PAYMENTS';
                ELSE
                BEGIN
                    UPDATE RentInvoices SET StatusId = 6 WHERE Id = @InvoiceId;
                    INSERT INTO InvoiceAudit(InvoiceId, Action, Reason, CreatedBy)
                    VALUES (@InvoiceId, 'CANCELLED', @Reason, @UserId);
                    SET @r = 'OK';
                END
                SELECT @r AS Result;";

            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, new[]
            {
                new SqlParameter("@InvoiceId", invoiceId),
                new SqlParameter("@Reason", reason),
                new SqlParameter("@UserId", userId),
            });
        }

        public async Task<ApiResponse> ReinstateInvoice(int invoiceId)
        {
            // A lease rent invoice for a month at/after the lease's billing end can only come back if the lease still
            // covers part of that month, and is re-priced to what it covered (so a month after move-out can't return).
            string query = @"
                SET XACT_ABORT ON;
                DECLARE @Ok BIT = 0, @NewAmount DECIMAL(18,2), @Descr NVARCHAR(200), @Reprice BIT = 0;
                SELECT @Ok = CASE WHEN ri.LeaseId IS NULL OR ri.ChargeType IS NOT NULL OR l.BilledThrough IS NULL
                                       OR ri.InvoiceMonth < DATEFROMPARTS(YEAR(l.BilledThrough), MONTH(l.BilledThrough), 1)
                                       OR c.Amount > 0 THEN 1 ELSE 0 END,
                       @Reprice = CASE WHEN ri.LeaseId IS NOT NULL AND ri.ChargeType IS NULL AND l.BilledThrough IS NOT NULL
                                       AND ri.InvoiceMonth >= DATEFROMPARTS(YEAR(l.BilledThrough), MONTH(l.BilledThrough), 1)
                                       THEN 1 ELSE 0 END,
                       @NewAmount = c.Amount, @Descr = c.Descr
                FROM RentInvoices ri
                LEFT JOIN TenantLeases l ON l.LeaseId = ri.LeaseId
                OUTER APPLY dbo.LeaseMonthCharge(ri.LeaseId, ri.InvoiceMonth) c
                WHERE ri.Id = @InvoiceId AND ri.StatusId = 6;

                IF @Ok = 0 BEGIN SELECT 'NOT_ALLOWED' AS Result; RETURN; END

                BEGIN TRAN;
                IF @Reprice = 1
                    UPDATE RentInvoices SET TotalRent = @NewAmount, Description = @Descr WHERE Id = @InvoiceId;

                INSERT INTO InvoiceAudit(InvoiceId, Action)
                SELECT Id, 'REINSTATED' FROM RentInvoices WHERE Id = @InvoiceId AND StatusId = 6;

                " + InvoiceSql.Recalc + @"
                WHERE ri.Id = @InvoiceId AND ri.StatusId = 6;
                COMMIT;
                SELECT 'OK' AS Result;";

            // Explicit result: the lookup SELECT above would make a row-count based success check unreliable
            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(query, new[] { new SqlParameter("@InvoiceId", invoiceId) });
            return new ApiResponse { IsSuccess = dt.Rows.Count > 0 && dt.Rows[0]["Result"].ToString() == "OK" };
        }
    }
}
