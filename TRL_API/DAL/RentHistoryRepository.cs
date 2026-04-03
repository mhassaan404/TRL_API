using System.Data;
using TRL_API.Data;

namespace TRL_API.DAL
{
    public class RentHistoryRepository
    {
        private readonly DbHelper _dbHelper;

        public RentHistoryRepository(DbHelper dbHelper)
        {
            _dbHelper = dbHelper;
        }

        public async Task<DataTable> GetHistoryAsync()
        {
            string query = @"
            SELECT 
                ri.Id AS Id,
                t.Name AS Tenant,
                u.UnitNumber AS Unit,
                ri.TotalRent AS MonthlyRent,
                ri.DueDate AS DueDate,
                MAX(p.PaymentDate) AS LastPaymentDate,
                STUFF((
                    SELECT DISTINCT ', ' + p2.PaymentMethod
                    FROM Payments p2
                    WHERE p2.RentInvoiceId = ri.Id
                    FOR XML PATH('')
                ), 1, 2, '') AS PaymentMethod,
                COALESCE(SUM(p.PaymentAmount), 0) AS TotalPaid,
                ri.TotalRent - COALESCE(SUM(p.PaymentAmount), 0) AS Balance,
                COALESCE(sl.StatusName, 'Unknown') AS Status,
                STUFF((
                    SELECT ' | ' + p2.Notes
                    FROM Payments p2
                    WHERE p2.RentInvoiceId = ri.Id AND p2.Notes IS NOT NULL
                    FOR XML PATH('')
                ), 1, 3, '') AS AllNotes
            FROM RentInvoices ri
            INNER JOIN Tenants t ON ri.TenantId = t.TenantId
            LEFT JOIN Units u ON t.UnitId = u.UnitId
            LEFT JOIN Payments p ON ri.Id = p.RentInvoiceId
            LEFT JOIN StatusList sl ON ri.StatusId = sl.StatusId
            WHERE ri.StatusId IN (1, 2, 3, 4, 5)
            GROUP BY 
                ri.Id,
                t.Name,
                u.UnitNumber,
                ri.TotalRent,
                ri.DueDate,
                sl.StatusName
            ORDER BY COALESCE(MAX(p.PaymentDate), ri.DueDate) DESC;
            ";

            return await _dbHelper.ExecuteQueryAsync(query);
        }
    }
}
