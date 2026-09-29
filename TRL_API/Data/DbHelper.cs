using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.Models;

namespace TRL_API.Data
{
    public class DbHelper
    {
        private readonly string _connectionString;

        public DbHelper(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")!;
        }

        public async Task<SqlConnection> GetOpenConnectionAsync()
        {
            var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            return conn;
        }


        public async Task<DataTable> ExecuteQueryReturnDataTableAsync(string commandText,
            SqlParameter[]? parameters = null, bool isStoredProcedure = false)
        {
            using var con = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(commandText, con);

            if (isStoredProcedure)
                cmd.CommandType = CommandType.StoredProcedure;

            if (parameters != null)
                cmd.Parameters.AddRange(parameters);

            cmd.CommandTimeout = 120;

            await con.OpenAsync();

            using var adapter = new SqlDataAdapter(cmd);

            var dt = new DataTable();
            adapter.Fill(dt);

            return dt;
        }

        public async Task<ApiResponse> ExecuteQueryAsync(string commandText, SqlParameter[]? parameters = null, 
            SqlConnection? conn = null, SqlTransaction? transaction = null, bool isStoredProcedure = false)
        {
            var result = new ApiResponse();
            bool ownConnection = conn == null;

            try
            {
                if (ownConnection)
                {
                    conn = new SqlConnection(_connectionString);
                    await conn.OpenAsync();
                }

                using var cmd = new SqlCommand(commandText, conn, transaction);

                if (isStoredProcedure)
                    cmd.CommandType = CommandType.StoredProcedure;

                if (parameters != null)
                    cmd.Parameters.AddRange(parameters);

                cmd.CommandTimeout = 120;

                int rowsAffected = await cmd.ExecuteNonQueryAsync();

                // Every caller targets specific rows, so 0 rows means the record wasn't found / wasn't in a valid state.
                // SQL errors are not caught here: they propagate so callers can handle SqlException numbers (2601, 547, ...).
                // -1 means no insert/update/delete ran at all (e.g. the batch exited early), which is also a failure
                result.IsSuccess = rowsAffected > 0;
                result.RowsAffected = rowsAffected;
                if (!result.IsSuccess)
                    result.ErrorMessage = "No matching record was found.";
            }
            finally
            {
                if (ownConnection && conn != null)
                    await conn.CloseAsync();
            }

            return result;
        }


    }
}
