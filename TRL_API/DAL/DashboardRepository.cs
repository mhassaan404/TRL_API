using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.Data;

namespace TRL_API.DAL
{
    public class DashboardRepository
    {
        private readonly DbHelper _dbHelper;

        public DashboardRepository(DbHelper dbHelper)
        {
            _dbHelper = dbHelper;
        }

        public async Task<DataTable> GetData(string month)
        {
            string query = @"
            SELECT 
                B.BuildingName,
                F.FloorNumber,
                U.UnitNumber,
                U.BaseRent AS UnitRent,
            	COALESCE(T.Name, '') TenantName,
                R.[Month],
                CASE 
                    WHEN TenantCount > 0 THEN CEILING(U.BaseRent / TenantCount)
                    ELSE 0
                END AS TotalRentPerTenant,
                COALESCE(R.PaidAmount, 0) PaidAmount,
                COALESCE(R.PendingAmount, 0) PendingAmount,
                COALESCE(R.Status, '') Status
            FROM Units U
            JOIN Floors F ON F.FloorId = U.FloorId
            JOIN Buildings B ON B.BuildingId = F.BuildingId
            LEFT JOIN Tenants T ON T.UnitId = U.UnitId
            LEFT JOIN RentInvoices R ON R.TenantId = T.TenantId
            OUTER APPLY (
                SELECT COUNT(*) AS TenantCount
                FROM Tenants t2
                JOIN RentInvoices r2 ON r2.TenantId = t2.TenantId
                WHERE t2.UnitId = U.UnitId
                  AND r2.[Month] = R.[Month]
            ) AS TC
            where R.[Month]=@Month
            ORDER BY B.BuildingName, F.FloorNumber, U.UnitNumber, R.[Month] DESC;";

            SqlParameter[] parameters =
            {
                new SqlParameter("@Month", string.IsNullOrEmpty(month) ? DBNull.Value : month)
            };

            var dt = await _dbHelper.ExecuteQueryAsync(query, parameters);
            return dt;
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
            
            SELECT 
                FORMAT(M.MonthStart, 'yyyy-MM') AS MonthYear,
            
                COUNT(DISTINCT RI.TenantId) AS TotalTenants,
            
                ISNULL(SUM(RI.TotalRent), 0) AS TotalRentDue,
            
                ISNULL(SUM(P.PaymentAmount), 0) AS CollectedAmount,
            
                ISNULL(SUM(RI.TotalRent) - SUM(ISNULL(P.PaymentAmount,0)), 0) AS PendingAmount
            
            FROM Months M
            
            LEFT JOIN RentInvoices RI 
                ON YEAR(RI.InvoiceDate) = YEAR(M.MonthStart)
                AND MONTH(RI.InvoiceDate) = MONTH(M.MonthStart)
            
            LEFT JOIN Payments P 
                ON P.RentInvoiceId = RI.Id
            
            GROUP BY M.MonthStart
            ORDER BY M.MonthStart;
            ";

            var dt = await _dbHelper.ExecuteQueryAsync(query);
            return dt;
        }


    }
}
