using System.Data;

namespace TRL_API.Models
{
    public static class DataRowExtensions
    {
        public static string GetString(this DataRow row, string columnName)
            => Convert.ToString(row[columnName]) ?? "";

        public static int GetInt(this DataRow row, string columnName)
            => row[columnName] != DBNull.Value ? Convert.ToInt32(row[columnName]) : 0;

        public static int? GetNullableInt(this DataRow row, string columnName)
            => row[columnName] != DBNull.Value ? Convert.ToInt32(row[columnName]) : (int?)null;

        public static decimal GetDecimal(this DataRow row, string columnName)
            => row[columnName] != DBNull.Value ? Convert.ToDecimal(row[columnName]) : 0m;

        public static DateTime GetDateTime(this DataRow row, string columnName)
            => row[columnName] != DBNull.Value ? Convert.ToDateTime(row[columnName]) : DateTime.MinValue;

        public static DateTime? GetNullableDateTime(this DataRow row, string columnName)
            => row[columnName] != DBNull.Value ? Convert.ToDateTime(row[columnName]) : (DateTime?)null;

        public static bool GetBool(this DataRow row, string columnName)
            => row[columnName] != DBNull.Value && Convert.ToBoolean(row[columnName]);
    }
}
