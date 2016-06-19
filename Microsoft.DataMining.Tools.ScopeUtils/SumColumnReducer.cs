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
            Dictionary<string, string> log = new Dictionary<string,string>();

            foreach (Row row in input.Rows)
            {
                foreach (ColumnInfo item in row.Schema.Columns)
                {
                    if(item.Name == "log")
                    {
                        foreach(string i in (row[item.Name].String??string.Empty).Split(';'))
                        {
                            List<string> pair = i.Split(':').ToList();
                            if (pair.Count() > 1)
                            {
                                if (!log.ContainsKey(pair[0]))
                                {
                                    log.Add(pair[0], pair[1]);
                                }
                            }
                        }
                    }
                    else
                    {
                        if (!columns.ContainsKey(item.Name))
                        {
                            columns.Add(item.Name, 0);
                        }

                        columns[item.Name] = columns[item.Name] + row[item.Name].LongQ ?? 0;
                    }
                }
            }

            foreach (string name in columns.Keys)
            {
                outputRow[name].Set(columns[name]);
            }

            outputRow["log"].Set(string.Join(";", log.Select(x => string.Format("{0}:{1}", x.Key, x.Value))));

            yield return outputRow;
        }

    }
}
