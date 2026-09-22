using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.Data;
using TRL_API.Models;

namespace TRL_API.DAL
{
    public class RentHistoryRepository
    {
        private readonly DbHelper _dbHelper;

        public RentHistoryRepository(DbHelper dbHelper)
        {
            _dbHelper = dbHelper;
        }

        //public async Task<DataTable> GetHistoryAsync()
        //{
        //    string query = @"
        //        SELECT 
        //            ri.Id AS invoiceId,
        //            ri.InvoiceDate,
        //            t.Name AS Tenant,
        //            u.UnitNumber AS Unit,
        //            ri.TotalRent AS MonthlyRent,
        //            MAX(p.PaymentDate) AS LastPaymentDate,

        //            COALESCE(
        //                STUFF((
        //                    SELECT DISTINCT ', ' + p2.PaymentMethod
        //                    FROM Payments p2
        //                    WHERE p2.RentInvoiceId = ri.Id
        //                    FOR XML PATH('')
        //                ), 1, 2, ''),
        //                'N/A'
        //            ) AS PaymentMethod,

        //            COALESCE(sl.StatusName, 'Unknown') AS Status
        //        FROM RentInvoices ri
        //        INNER JOIN Tenants t 
        //            ON ri.TenantId = t.TenantId
        //        LEFT JOIN Units u 
        //            ON t.UnitId = u.UnitId
        //        LEFT JOIN Payments p 
        //            ON ri.Id = p.RentInvoiceId
        //        LEFT JOIN StatusList sl 
        //            ON ri.StatusId = sl.StatusId

        //        GROUP BY 
        //            ri.Id,
        //            ri.InvoiceDate,
        //            t.Name,
        //            u.UnitNumber,
        //            ri.TotalRent,
        //            sl.StatusName

        //        ORDER BY ri.InvoiceDate DESC;";

        //    return await _dbHelper.ExecuteQueryReturnDataTableAsync(query);
        //}

        //public async Task<ApiResponse> CancelInvoice(int invoiceId)
        //{
        //    string query = @"
        //        UPDATE RentInvoices
        //        SET StatusId = 6
        //        WHERE Id = @InvoiceId
        //          AND StatusId <> 6;";

        //    var parameters = new[]
        //    {
        //        new SqlParameter("@InvoiceId", invoiceId)
        //    };
        //    return await _dbHelper.ExecuteQueryAsync(query, parameters);
        //}

        //public async Task<ApiResponse> ReinstateInvoice(int invoiceId)
        //{
        //    string query = @"
        //        UPDATE ri
        //        SET
        //            PendingAmount = CASE
        //                WHEN (ri.TotalRent - ISNULL(pt.Paid, 0) - ISNULL(pt.Disc, 0)) < 0
        //                    THEN 0
        //                ELSE (ri.TotalRent - ISNULL(pt.Paid, 0) - ISNULL(pt.Disc, 0))
        //            END,

        //            OverPaidAmount = CASE
        //                WHEN (ri.TotalRent - ISNULL(pt.Paid, 0) - ISNULL(pt.Disc, 0)) < 0
        //                    THEN ABS(ri.TotalRent - ISNULL(pt.Paid, 0) - ISNULL(pt.Disc, 0))
        //                ELSE 0
        //            END,

        //            StatusId = CASE
        //                WHEN (ri.TotalRent - ISNULL(pt.Paid, 0) - ISNULL(pt.Disc, 0)) < 0 THEN 9
        //                WHEN (ri.TotalRent - ISNULL(pt.Paid, 0) - ISNULL(pt.Disc, 0)) = 0 THEN 1
        //                WHEN (ri.TotalRent - ISNULL(pt.Paid, 0) - ISNULL(pt.Disc, 0)) < ri.TotalRent THEN 8
        //                ELSE 2
        //            END

        //        FROM RentInvoices ri

        //        OUTER APPLY (
        //            SELECT
        //                SUM(PaymentAmount) AS Paid,
        //                SUM(DiscountAmount) AS Disc
        //            FROM Payments
        //            WHERE RentInvoiceId = ri.Id
        //        ) pt

        //        WHERE ri.Id = @InvoiceId
        //          AND ri.StatusId = 6;";

        //    var parameters = new[]
        //    {
        //        new SqlParameter("@InvoiceId", invoiceId)
        //    };

        //    return await _dbHelper.ExecuteQueryAsync(query, parameters);
        //}

        public async Task<DataTable> GetHistoryAsync()
        {
            string query = @"
                SELECT ri.Id AS invoiceId, ri.InvoiceDate, t.Name AS Tenant, u.UnitNumber AS Unit,
                       ri.TotalRent AS MonthlyRent, ri.LateFeeCharged, ri.ChargeType,
                       pt.Paid AS PaidAmount, pt.Disc AS DiscountAmount,
                       CASE WHEN ri.StatusId = 6 THEN 0
                            ELSE ri.TotalRent + ri.LateFeeCharged - pt.Paid - pt.Disc END AS Balance,
                       pt.LastPaymentDate,
                       COALESCE(pt.Methods, 'N/A') AS PaymentMethod,
                       COALESCE(sl.StatusName, 'Unknown') AS Status
                FROM RentInvoices ri
                INNER JOIN Tenants t ON ri.TenantId = t.TenantId
                LEFT JOIN Units u ON t.UnitId = u.UnitId
                LEFT JOIN StatusList sl ON ri.StatusId = sl.StatusId
                OUTER APPLY (
                    SELECT ISNULL(SUM(p.PaymentAmount), 0) AS Paid,
                           ISNULL(SUM(p.DiscountAmount), 0) AS Disc,
                           MAX(p.PaymentDate) AS LastPaymentDate,
                           STUFF((SELECT DISTINCT ', ' + p2.PaymentMethod FROM Payments p2
                                  WHERE p2.RentInvoiceId = ri.Id AND p2.PaymentMethod IS NOT NULL
                                  FOR XML PATH('')), 1, 2, '') AS Methods
                    FROM Payments p WHERE p.RentInvoiceId = ri.Id
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
            string query = @"
                INSERT INTO InvoiceAudit(InvoiceId, Action)
                SELECT Id, 'REINSTATED' FROM RentInvoices WHERE Id = @InvoiceId AND StatusId = 6;

                UPDATE ri SET
                    PendingAmount  = CASE WHEN x.Bal < 0 THEN 0 ELSE x.Bal END,
                    OverPaidAmount = CASE WHEN x.Bal < 0 THEN -x.Bal ELSE 0 END,
                    StatusId = CASE WHEN x.Bal < 0 THEN 9
                                    WHEN x.Bal = 0 THEN 1
                                    WHEN p.Paid + p.Disc > 0 THEN 8
                                    ELSE 2 END
                FROM RentInvoices ri
                OUTER APPLY (SELECT ISNULL(SUM(PaymentAmount),0) AS Paid, ISNULL(SUM(DiscountAmount),0) AS Disc
                             FROM Payments WHERE RentInvoiceId = ri.Id) p
                CROSS APPLY (SELECT ri.TotalRent + ri.LateFeeCharged - p.Paid - p.Disc AS Bal) x
                WHERE ri.Id = @InvoiceId AND ri.StatusId = 6;";

            return await _dbHelper.ExecuteQueryAsync(query, new[] { new SqlParameter("@InvoiceId", invoiceId) });
        }
    }
}
