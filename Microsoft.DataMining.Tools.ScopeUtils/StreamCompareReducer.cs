using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using ScopeRuntime;


namespace Microsoft.DataMining.Tools.ScopeUtils
{
    public class StreamCompareReducer: Reducer
    {
        private const string conf = @"
<PrimaryKeys>
  <SafeList>
    <TanantTag>Tag</TanantTag>
    <AssignedPlan>ServicePlanId,SubscribedPlanId,ServiceInstance</AssignedPlan>
    <PartnerTenant>SyndicationPartnerId</PartnerTenant>
    <Subscription>Id</Subscription>
    <Domain>Name</Domain>
    <TopParent>TopParentId</TopParent>
    <SubscriptionChangeEvent>Type,EventDate</SubscriptionChangeEvent>
    <ServicePlan>ServicePlanId</ServicePlan>
  </SafeList>
</PrimaryKeys>
";

        private Dictionary<string, List<string>> PrimaryKeys = new Dictionary<string, List<string>>();
        private List<string> ReducerKeys = new List<string>();
        private List<string> OutputSchema = new List<string>();

        public StreamCompareReducer(string reducerKeys, string conf = StreamCompareReducer.conf)
        {
            this.ReducerKeys = new List<string>(reducerKeys.Split(','));
            this.PrimaryKeys = ParseConfXml(conf, "SafeList");
        }

        public override Schema Produces(string[] requestedColumns, string[] args, Schema input)
        {
            if(this.OutputSchema.Count() <= 0)
            {
                this.OutputSchema = GetOutputSchema(input);
            }

            return new Schema(string.Format("{0},log:string",string.Join(",", this.OutputSchema.Select(x => string.Format("{0}:long", x)))));
        }

        public override IEnumerable<Row> Reduce(RowSet input, Row outputRow, string[] args)
        {
            if (this.OutputSchema.Count() <= 0)
            {
                this.OutputSchema = GetOutputSchema(input.Schema);
            }

            Row former = null;
            Row latter = null;

            List<string> log = new List<string>();
            string logPrimaryKeyValues =string.Empty;
            int count = 0;
            foreach(Row row in input.Rows)
            {
                if (row["IsFormer"].Boolean)
                {
                    former = row.Clone();
                    logPrimaryKeyValues = GetPrimaryKeyValues(former);
                }
                else
                {
                    latter = row.Clone();
                    logPrimaryKeyValues = GetPrimaryKeyValues(latter);
                }

                count++;
            }

            if (count > 2)
            {
                throw new ArgumentException("The Primary Key is Worry for Reducer!");
            }

            foreach (ColumnInfo item in outputRow.Schema.Columns)
            {
                outputRow[item.Name].Set(0L);
            }

            if (count <= 0)
            {
                // Do nothing;
            }
            else if (count == 1)
            {
                if (former == null)
                {
                    outputRow["Root_PrimaryKey_Add"].Set(1L);
                    log.Add(string.Format("{0}:{1}", "Root_PrimaryKey_Add", logPrimaryKeyValues));
                }
                else
                {
                    outputRow["Root_PrimaryKey_Del"].Set(1L);
                    log.Add(string.Format("{0}:{1}", "Root_PrimaryKey_Del", logPrimaryKeyValues));
                }
            }
            else 
            {
                foreach(string col in this.OutputSchema)
                {
                    if (col == "_IsFormer" || col == "Root_PrimaryKey_Add" || col == "Root_PrimaryKey_Del")
                    {
                        continue;
                    }

                    List<string> schema = col.Split('_').ToList();

                    long unMatchCount = CompareObject(former[schema[0]].Value, latter[schema[0]].Value, schema);
                    outputRow[col].Set(unMatchCount);

                    if (unMatchCount != 0)
                    {
                        log.Add(string.Format("{0}:{1}", col, logPrimaryKeyValues));
                    }
                }
            }

            outputRow["log"].Set(string.Join(";", log));

            yield return outputRow;
        }

        private long CompareObject(object former, object latter, List<string> schema)
        {
            if (former == null && latter == null)
            {
                return 0;
            }

            Type formerType = former.GetType();
            Type latterType = latter.GetType();

            if (formerType != latterType)
            {
                throw new ArgumentException("CompareObject sholud compare the same class object!");
            }

            Type type = formerType;

            if (schema.Count() == 1)
            {
                return former.Equals(latter) ? 0 : 1;
            }
            else if (schema.Count() >= 2 && schema[1] == "List")
            {
                Dictionary<string, object> formerPrimaryKeys = new Dictionary<string, object>();

                foreach (object item in (IEnumerable<object>)former)
                {
                    string key = GetPrimaryKeyValues(item);

                    if (formerPrimaryKeys.ContainsKey(key))
                    {
                        formerPrimaryKeys[key] = item;
                    }
                    else
                    {
                        formerPrimaryKeys.Add(key, item);
                    }
                }

                if (schema.Count() == 3 && schema[2] == "PrimaryKey")
                {
                    long commonKeyCount = 0;
                    foreach (object item in (IEnumerable<object>)latter)
                    {
                        string key = GetPrimaryKeyValues(item);

                        if (formerPrimaryKeys.ContainsKey(key))
                        {
                            commonKeyCount++;
                        }
                    }

                    if (schema.Count() == 4 && schema[3] == "Add")
                    {
                        return ((IEnumerable<object>)latter).Count() - commonKeyCount;
                    }
                    else if (schema.Count() == 4 && schema[3] == "Del")
                    {
                        return ((IEnumerable<object>)former).Count() - commonKeyCount;
                    }
                    else
                    {
                        throw new ArgumentException(string.Format("Output schema {0} is not format!", string.Join("_", schema.ToList())));
                    }
                }
                else if (schema.Count() == 3 && schema[2] != "PrimaryKey")
                {
                    long output = 0L;
                    foreach (object item in (IEnumerable<object>)latter)
                    {
                        string key = GetPrimaryKeyValues(item);

                        if (formerPrimaryKeys.ContainsKey(key))
                        {
                            string propName = schema[2];
                            object formerValue = GetPropertyValue(formerPrimaryKeys[key], propName);
                            object latterValue = GetPropertyValue(item, propName);

                            output += CompareObject(formerValue, latterValue, schema.GetRange(2, schema.Count() - 2));
                        }
                    }

                    return output;
                }
                else
                {
                    return 0L;
                }
            }
            else
            {
                string propName = schema[1];
                object formerValue = GetPropertyValue(former, propName);
                object latterValue = GetPropertyValue(latter, propName);

                return CompareObject(formerValue, latterValue, schema.GetRange(1, schema.Count() - 1));
            }
        }


        private string GetPrimaryKeyValues(object src)
        {
            List<string> primaryKeysList = new List<string>();

            Type type = src.GetType();

            if (src is Row)
            {
                Row row = (Row)src;
                return string.Join(",", this.ReducerKeys.Select(x => row[x].Value.ToString()));
            }
            else if(this.PrimaryKeys.ContainsKey(type.Name))
            {
                primaryKeysList.AddRange(this.PrimaryKeys[type.Name]);
            }
            else
            {
                primaryKeysList.AddRange(GetObjectProperties(type).Select(x => x.Name));
            }

            return string.Join(",", primaryKeysList.Select(x => GetPropertyValue(src, x).ToString()));
        }

        private object GetPropertyValue(object src, string propName)
        {
            if (src == null)
            {
                return null;
            }

            try
            {
                return src.GetType().GetProperty(propName).GetValue(src, null);
            }
            catch
            {
                throw new ArgumentException(string.Format("{0} don't have property {1}!", src.GetType(), propName));
            }
            
        }

        private IEnumerable<PropertyInfo> GetObjectProperties(Type type)
        {
            IOrderedEnumerable<PropertyInfo> properties =
                type.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(x => x.Name);

            foreach (PropertyInfo prop in properties)
            {
                if (prop.Name != "Item")
                {
                    yield return prop;
                }
            }
        }


        private List<string> GetOutputSchema(Schema input)
        {
            List<string> output = new List<string>();

            output.AddRange(GetPrimaryKeySchema("Root"));
            foreach(ColumnInfo item in input.Columns)
            {
                if (this.PrimaryKeys.ContainsKey(item.Name) || this.ReducerKeys.Contains(item.Name))
                {
                    continue;
                }
                else
                {
                    output.AddRange(GetPropertySchema(item.ColumnCLRType, item.Name));
                }
            }

            return output;
        }

        private IEnumerable<string> GetPrimaryKeySchema(string name)
        {
            yield return string.Format("{0}_PrimaryKey_Add", name);
            yield return string.Format("{0}_PrimaryKey_Del", name);
        }

        private IEnumerable<string> GetPropertySchema(Type type, string name, bool isListProperty = false)
        {
            if (type.GetInterface("IEnumerable") != null && type != typeof(string))
            {
                Type[] types = type.GetGenericArguments();
                if (types.Count() == 1)
                {
                    foreach (string schema in GetPropertySchema(types[0], string.Format("{0}_List", name), true))
                    {
                        yield return schema;
                    }
                }
            }
            else if (type.Namespace.StartsWith("System"))
            {
                yield return name;
            }
            else
            {
                if (isListProperty)
                {
                    foreach (string s in GetPrimaryKeySchema(name))
                    {
                        yield return s;
                    }

                    if (this.PrimaryKeys.ContainsKey(type.Name))
                    {
                        IOrderedEnumerable<PropertyInfo> properties =
                            type.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(x => x.Name);

                        foreach (PropertyInfo prop in properties)
                        {
                            if (this.PrimaryKeys[type.Name].Contains(prop.Name) || prop.Name == "Item")
                            {
                                continue;
                            }
                            else
                            {
                                foreach (string schema in GetPropertySchema(prop.PropertyType, prop.Name))
                                {
                                    yield return string.Format("{0}_{1}", name, schema);
                                }
                            }
                        }
                    }
                }
                else
                {
                    IOrderedEnumerable<PropertyInfo> properties =
                            type.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(x => x.Name);

                    foreach (PropertyInfo prop in properties)
                    {
                        if (prop.Name == "Item")
                        {
                            continue;
                        }
                        else
                        {
                            foreach (string schema in GetPropertySchema(prop.PropertyType, prop.Name))
                            {
                                yield return string.Format("{0}_{1}", name, schema);
                            }
                        }
                    }
                }
            }
        }

        private Dictionary<string, List<string>> ParseConfXml(string xmlString, string listName)
        {
            Dictionary<string, List<string>> output = new Dictionary<string, List<string>>();

            XmlDocument xDoc = new XmlDocument();
            xDoc.LoadXml(conf);

            foreach (XmlNode i in xDoc.DocumentElement.ChildNodes)
            {
                if (i.Name == listName)
                {
                    foreach (XmlNode j in i.ChildNodes)
                    {
                        if (j.NodeType == XmlNodeType.Comment)
                        {
                            continue;
                        }

                        List<string> values = new List<string>(j.InnerText.Split(','));

                        if (output.ContainsKey(j.Name))
                        {
                            output[j.Name] = values;
                        }
                        else
                        {
                            output.Add(j.Name, values);
                        }
                    }
                }
            }

            return output;
        }
    }
}
