using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ScopeRuntime;

namespace Microsoft.DataMining.Tools.ScopeUtils
{
    public class SumColumnReducer : Reducer
    {
        public override bool IsRecursive
        {
            get
            {
                return true;
            }
        }

        public override Schema Produces(string[] requestedColumns, string[] args, Schema input)
        {
            return input.CloneWithSource();
        }

        public override IEnumerable<Row> Reduce(RowSet input, Row outputRow, string[] args)
        {
            Dictionary<string, long> columns = new Dictionary<string, long>();

            foreach (Row row in input.Rows)
            {
                foreach (ColumnInfo item in row.Schema.Columns)
                {
                    if (item.Name.StartsWith("DateTime_") || item.Name.StartsWith("Dim_"))
                    {
                        outputRow[item.Name].Set(row[item.Name].Value);

                        continue;
                    }

                    if (!columns.ContainsKey(item.Name))
                    {
                        columns.Add(item.Name, 0);
                    }

                    columns[item.Name] = columns[item.Name] + row[item.Name].LongQ ?? 0;
                }
            }

            foreach (string name in columns.Keys)
            {
                outputRow[name].Set(columns[name]);
            }

            yield return outputRow;
        }

    }
}
