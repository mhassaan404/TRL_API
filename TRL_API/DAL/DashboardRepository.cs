using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.Data;

namespace TRL_API.DAL
{
    public class DashboardRepository : IDashboardRepository
    {
        private readonly DbHelper _dbHelper;

        public DashboardRepository(DbHelper dbHelper)
        {
            _dbHelper = dbHelper;
        }

        public async Task<DataTable> GetDashboardData()
        {
            string query = @"
            ;WITH Months AS
            (
                SELECT 
                    DATEADD(MONTH, -v.number, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)) AS MonthStart
                FROM master..spt_values v
                WHERE v.type = 'P' AND v.number BETWEEN 0 AND 6
            )
            
            -- Payments are totalled per invoice first so an invoice's rent is counted once, not once per payment.
            -- Cancelled invoices (StatusId 6) are excluded; charged late fees count as amounts due.
            SELECT
                FORMAT(M.MonthStart, 'yyyy-MM') AS MonthYear,

                COUNT(DISTINCT RI.TenantId) AS TotalTenants,

                ISNULL(SUM(RI.TotalRent + RI.LateFeeCharged), 0) AS TotalRentDue,

                ISNULL(SUM(BAL.Paid), 0) AS CollectedAmount,

                ISNULL(SUM(CASE WHEN BAL.Balance > 0 THEN BAL.Balance ELSE 0 END), 0) AS PendingAmount

            FROM Months M

            LEFT JOIN RentInvoices RI
                ON YEAR(RI.InvoiceDate) = YEAR(M.MonthStart)
                AND MONTH(RI.InvoiceDate) = MONTH(M.MonthStart)
                AND RI.StatusId <> 6

            OUTER APPLY dbo.InvoiceBalance(RI.Id, 0) BAL

            GROUP BY M.MonthStart
            ORDER BY M.MonthStart;
            ";

            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(query);
            return dt;
        }


    }
}
