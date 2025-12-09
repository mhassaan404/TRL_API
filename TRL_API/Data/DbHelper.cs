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



        public async Task<int> ExecuteCommandAsync(
    string query,
    SqlParameter[]? parameters = null,
    SqlConnection? conn = null,
    SqlTransaction? transaction = null,
    bool isStoredProc = false)
        {
            bool ownConnection = conn == null;
            if (ownConnection) conn = new SqlConnection(_connectionString);
            if (ownConnection) await conn!.OpenAsync();

            using var cmd = new SqlCommand(query, conn, transaction);
            if (isStoredProc) cmd.CommandType = CommandType.StoredProcedure;
            if (parameters != null) cmd.Parameters.AddRange(parameters);

            int rowsAffected = await cmd.ExecuteNonQueryAsync();
            if (ownConnection) await conn!.CloseAsync();
            return rowsAffected > 0 ? 1 : 0;
        }

        // Executes multiple commands in a transaction
        public async Task<int> ExecuteTransactionAsync(Func<SqlConnection, SqlTransaction, Task<int>> action)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var transaction = conn.BeginTransaction();
            try
            {
                int result = await action(conn, transaction);
                transaction.Commit();
                return result;
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                throw new Exception($"Transaction failed: {ex.Message}", ex);
            }
        }




        //public async Task<int> ExecuteCommandAsync(string query, SqlParameter[]? parameters = null, bool isStoredProc = false)
        //{
        //    try
        //    {
        //        using (var conn = new SqlConnection(_connectionString))
        //        {
        //            await conn.OpenAsync();

        //            using (var cmd = new SqlCommand(query, conn))
        //            {
        //                if (isStoredProc)
        //                    cmd.CommandType = CommandType.StoredProcedure;

        //                if (parameters != null)
        //                    cmd.Parameters.AddRange(parameters);

        //                int rowsAffected = await cmd.ExecuteNonQueryAsync();

        //                // return 1 if at least 1 row affected, otherwise 0
        //                return rowsAffected > 0 ? 1 : 0;
        //            }
        //        }
        //    }
        //    catch (SqlException ex)
        //    {
        //        throw new Exception($"SQL Error: {ex.Message}", ex);
        //    }
        //}

        //public async Task<int> ExecuteCommandAsync(string query, SqlParameter[]? parameters, SqlConnection conn, SqlTransaction transaction, bool isStoredProc = false)
        //{
        //    using (var cmd = new SqlCommand(query, conn, transaction))
        //    {
        //        if (isStoredProc)
        //            cmd.CommandType = CommandType.StoredProcedure;

        //        if (parameters != null)
        //            cmd.Parameters.AddRange(parameters);

        //        int rowsAffected = await cmd.ExecuteNonQueryAsync();
        //        return rowsAffected > 0 ? 1 : 0;
        //    }
        //}

        //public async Task<int> ExecuteTransactionAsync(Func<SqlConnection, SqlTransaction, Task<int>> action)
        //{
        //    using var conn = new SqlConnection(_connectionString);
        //    await conn.OpenAsync();

        //    using var transaction = conn.BeginTransaction();
        //    try
        //    {
        //        int result = await action(conn, transaction);
        //        transaction.Commit();
        //        return result;
        //    }
        //    catch (Exception ex)
        //    {
        //        transaction.Rollback();
        //        throw new Exception($"Transaction failed: {ex.Message}", ex);
        //    }
        //}

    }
}
