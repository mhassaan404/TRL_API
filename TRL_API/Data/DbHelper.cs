using Microsoft.Data.SqlClient;
using System.Data;

namespace TRL_API.Data
{
    public class DbHelper
    {
        private readonly string _connectionString;

        public DbHelper(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")!;
        }

        public async Task<DataTable> ExecuteQueryAsync(string query, SqlParameter[]? parameters = null, bool isStoredProc = false)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand(query, conn))
                {
                    if (isStoredProc)
                        cmd.CommandType = CommandType.StoredProcedure;

                    if (parameters != null)
                        cmd.Parameters.AddRange(parameters);

                    using (var adapter = new SqlDataAdapter(cmd))
                    {
                        var dt = new DataTable();
                        adapter.Fill(dt);
                        return dt;
                    }
                }
            }
        }

        public async Task<int> ExecuteCommandAsync(string query, SqlParameter[]? parameters = null, bool isStoredProc = false)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand(query, conn))
                    {
                        if (isStoredProc)
                            cmd.CommandType = CommandType.StoredProcedure;

                        if (parameters != null)
                            cmd.Parameters.AddRange(parameters);

                        return await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (SqlException ex)
            {
                throw new Exception($"SQL Error: {ex.Message}", ex);
            }
        }
    }
}
