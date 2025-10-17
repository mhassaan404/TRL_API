using System.Data;

namespace TRL_API.BLL
{
    public class DataTableHelper
    {
        public static List<Dictionary<string, object?>> ToDictionaryList(DataTable dt)
        {
            return [.. dt.AsEnumerable()
            .Select(row => dt.Columns.Cast<DataColumn>()
                .ToDictionary(
                    col => col.ColumnName,
                    col => row[col] == DBNull.Value ? null : row[col]
                ))
            ];
        }

    }
}
