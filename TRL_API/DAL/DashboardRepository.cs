using Microsoft.Data.SqlClient;
using System.Data;

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
                        WITH Months AS (
                SELECT FORMAT(DATEADD(MONTH, -0, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)), 'yyyy-MM') AS MonthYear
                UNION ALL
                SELECT FORMAT(DATEADD(MONTH, -1, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)), 'yyyy-MM')
                UNION ALL
                SELECT FORMAT(DATEADD(MONTH, -2, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)), 'yyyy-MM')
                UNION ALL
                SELECT FORMAT(DATEADD(MONTH, -3, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)), 'yyyy-MM')
                UNION ALL
                SELECT FORMAT(DATEADD(MONTH, -4, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)), 'yyyy-MM')
                UNION ALL
                SELECT FORMAT(DATEADD(MONTH, -5, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)), 'yyyy-MM')
                UNION ALL
                SELECT FORMAT(DATEADD(MONTH, -6, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)), 'yyyy-MM')
            )
            SELECT 
                M.MonthYear,
                ISNULL(COUNT(DISTINCT T.TenantId), 0) AS TotalTenants,
                ISNULL(SUM(R.TotalRent), 0) AS TotalRentDue,
                ISNULL(SUM(CASE WHEN R.Status = 'Paid' THEN R.PaidAmount ELSE 0 END), 0) AS CollectedAmount,
                ISNULL(SUM(CASE WHEN R.Status <> 'Paid' THEN R.PendingAmount ELSE 0 END), 0) AS PendingAmount
            FROM Months M
            LEFT JOIN RentInvoices R ON FORMAT(CONVERT(date, R.[Month] + '-01'), 'yyyy-MM') = M.MonthYear
            LEFT JOIN Tenants T ON T.TenantId = R.TenantId
            GROUP BY M.MonthYear;
            ";

            var dt = await _dbHelper.ExecuteQueryAsync(query);
            return dt;
        }


    }
}
