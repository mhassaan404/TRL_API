using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.Data;
using TRL_API.Models;

namespace TRL_API.DAL
{
    public class CompanyProfileRepository : ICompanyProfileRepository
    {
        private readonly DbHelper _dbHelper;
        public CompanyProfileRepository(DbHelper dbHelper) => _dbHelper = dbHelper;

        // The single profile row (created by migration 2026-10-09_company_profile.sql). A missing row is a
        // database setup error.
        public async Task<DataTable> GetAsync()
        {
            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT p.Phone, p.Email, p.Ntn, p.Address, p.Website, p.FooterNote, p.Logo, p.LogoContentType,
                       u.Username AS UpdatedBy, p.UpdatedAt
                FROM CompanyProfile p
                LEFT JOIN Users u ON u.UserId = p.UpdatedBy
                WHERE p.Id = 1;");
            if (dt.Rows.Count == 0)
                throw new InvalidOperationException("Company profile is missing. Apply migration 2026-10-09_company_profile.sql.");
            return dt;
        }

        public async Task<ApiResponse> SaveAsync(SaveCompanyProfileRequest p, LogoChange logo, byte[]? logoBytes, string? logoType, int userId)
        {
            var logoSql = logo switch
            {
                LogoChange.Replace => ", Logo = @Logo, LogoContentType = @LogoType",
                LogoChange.Remove => ", Logo = NULL, LogoContentType = NULL",
                _ => "",
            };
            var parameters = new List<SqlParameter>
            {
                Text("@Phone", p.Phone, 30),
                Text("@Email", p.Email, 100),
                Text("@Ntn", p.Ntn, 30),
                Text("@Address", p.Address, 300),
                Text("@Website", p.Website, 150),
                Text("@FooterNote", p.FooterNote, 200),
                new("@UserId", userId),
            };
            if (logo == LogoChange.Replace)
            {
                parameters.Add(new SqlParameter("@Logo", SqlDbType.VarBinary, -1) { Value = logoBytes! });
                parameters.Add(new SqlParameter("@LogoType", SqlDbType.VarChar, 20) { Value = logoType! });
            }

            return await _dbHelper.ExecuteQueryAsync($@"
                UPDATE CompanyProfile
                SET Phone = @Phone, Email = @Email, Ntn = @Ntn, Address = @Address, Website = @Website,
                    FooterNote = @FooterNote{logoSql}, UpdatedBy = @UserId, UpdatedAt = GETDATE()
                WHERE Id = 1;",
                parameters.ToArray());
        }

        private static SqlParameter Text(string name, string? value, int size) =>
            new(name, SqlDbType.NVarChar, size) { Value = (object?)value ?? DBNull.Value };
    }
}
