using System.Data;

namespace TRL_API.Helpers
{
    public class DataTableHelper
    {
        //public static List<Dictionary<string, object?>> ToDictionaryList(DataTable dt)
        //{
        //    return [.. dt.AsEnumerable()
        //    .Select(row => dt.Columns.Cast<DataColumn>()
        //        .ToDictionary(
        //            col => col.ColumnName,
        //            col => row[col] == DBNull.Value ? null : row[col]
        //        ))
        //    ];
        //}

        public static List<Dictionary<string, object?>> ToDictionaryList(DataTable dt, bool useCamelCase = false)
        {
            var list = new List<Dictionary<string, object?>>(dt.Rows.Count);

            foreach (DataRow row in dt.Rows)
            {
                var dict = new Dictionary<string, object?>(dt.Columns.Count);

                foreach (DataColumn col in dt.Columns)
                {
                    // Decide key format
                    var key = useCamelCase
                        ? Char.ToLowerInvariant(col.ColumnName[0]) + col.ColumnName.Substring(1)
                        : col.ColumnName;

                    // DBNull → null
                    dict[key] = row[col] == DBNull.Value ? null : row[col];
                }

                list.Add(dict);
            }

            return list;
        }

        //public static List<Dictionary<string, object?>> ToDictionaryListCamelCase(DataTable dt)
        //{
        //    var list = new List<Dictionary<string, object?>>(dt.Rows.Count);

        //    foreach (DataRow row in dt.Rows)
        //    {
        //        var dict = new Dictionary<string, object?>(dt.Columns.Count);

        //        foreach (DataColumn col in dt.Columns)
        //        {
        //            // Convert column name to camelCase
        //            var key = Char.ToLowerInvariant(col.ColumnName[0]) + col.ColumnName.Substring(1);

        //            // DBNull → null
        //            dict[key] = row[col] == DBNull.Value ? null : row[col];
        //        }

        //        list.Add(dict);
        //    }

        //    return list;
        //}

    }
}
