using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;


namespace MSSQLand.Actions.Database
{
    internal class Tables : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "db", LongName = "database", Description = "Database name (uses current database if not specified)")]
        private string _database;

        [ArgumentMetadata(Position = 1, ShortName = "n", LongName = "name", Description = "Filter tables by name pattern (supports wildcards %)")]
        private string _name = "";

        [ArgumentMetadata(Position = 2, LongName = "columns", Description = "Show column names for each table")]
        private bool _showColumns = false;

        public override void ValidateArguments(string[] args)
        {
            var (namedArgs, positionalArgs) = ParseActionArguments(args);
            
            // Get database from positional or named arguments
            _database = GetPositionalArgument(positionalArgs, 0);
            if (string.IsNullOrEmpty(_database))
            {
                _database = GetNamedArgument(namedArgs, "database", GetNamedArgument(namedArgs, "db", null));
            }
            
            // Get name filter from positional or named arguments
            _name = GetNamedArgument(namedArgs, "n", null)
                 ?? GetNamedArgument(namedArgs, "name", null)
                 ?? GetPositionalArgument(positionalArgs, 1, "");
            
            // Check for --columns flag
            _showColumns = namedArgs.ContainsKey("columns");
            
            // If still null, will use current database in Execute()
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            // Use the execution database if no database is specified
            string targetDatabase = string.IsNullOrEmpty(_database) 
                ? databaseContext.QueryService.ExecutionServer.Database 
                : _database;

            string filterMsg = !string.IsNullOrEmpty(_name) ? $" (name: {_name})" : "";
            string columnsMsg = _showColumns ? " with columns" : "";
            Logger.TaskNested($"Retrieving tables from [{targetDatabase}]{filterMsg}{columnsMsg}");

            // Build USE statement if specific database is provided
            string useStatement = string.IsNullOrEmpty(_database) ? "" : $"USE [{_database}];";

            // Build WHERE clause with filter
            string whereClause = "WHERE t.type IN ('U', 'V') AND p.index_id IN (0, 1)";
            if (!string.IsNullOrEmpty(_name))
            {
                whereClause += $" AND t.name LIKE '%{_name.Replace("'", "''")}%'";
            }

            string query = $@"
                {useStatement}
                SELECT 
                    s.name AS SchemaName,
                    t.name AS TableName,
                    t.type_desc AS TableType,
                    SUM(p.rows) AS Rows
                FROM 
                    sys.objects t
                JOIN 
                    sys.schemas s ON t.schema_id = s.schema_id
                LEFT JOIN 
                    sys.partitions p ON t.object_id = p.object_id
                {whereClause}
                GROUP BY 
                    s.name, t.name, t.type_desc
                ORDER BY 
                    SchemaName, TableName;";

            DataTable tables = databaseContext.QueryService.ExecuteTable(query);

            if (tables.Rows.Count == 0)
            {
                Logger.Warning("No tables found.");
                return tables;
            }

            // Get all permissions in a single query
            string allPermissionsQuery = $@"{useStatement}SELECT SCHEMA_NAME(o.schema_id) AS schema_name, o.name AS object_name, p.permission_name FROM sys.objects o CROSS APPLY fn_my_permissions(QUOTENAME(SCHEMA_NAME(o.schema_id)) + '.' + QUOTENAME(o.name), 'OBJECT') p WHERE o.type IN ('U', 'V') ORDER BY o.name, p.permission_name;";

            DataTable allPermissions = databaseContext.QueryService.ExecuteTable(allPermissionsQuery);

            // Build a dictionary for fast lookup: key = "schema.table", value = set of unique permissions
            var permissionsDict = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>>();
            
            foreach (DataRow permRow in allPermissions.Rows)
            {
                string key = $"{permRow["schema_name"]}.{permRow["object_name"]}";
                string permission = permRow["permission_name"].ToString();

                if (!permissionsDict.ContainsKey(key))
                {
                    permissionsDict[key] = new System.Collections.Generic.HashSet<string>();
                }
                permissionsDict[key].Add(permission);
            }

            // Add a column for permissions
            tables.Columns.Add("Permissions", typeof(string));

            // Map permissions to tables
            foreach (DataRow row in tables.Rows)
            {
                string schemaName = row["SchemaName"].ToString();
                string tableName = row["TableName"].ToString();
                string key = $"{schemaName}.{tableName}";

                if (permissionsDict.TryGetValue(key, out var permissions))
                {
                    row["Permissions"] = string.Join(", ", permissions);
                }
                else
                {
                    row["Permissions"] = "";
                }
            }

            // Optionally add column names if --columns flag is set
            if (_showColumns)
            {
                // Query to get columns for all tables
                string columnsQuery = $@"{useStatement}
SELECT 
    SCHEMA_NAME(t.schema_id) AS schema_name,
    t.name AS table_name,
    c.name AS column_name,
    TYPE_NAME(c.user_type_id) AS data_type,
    c.column_id
FROM sys.tables t
INNER JOIN sys.columns c ON t.object_id = c.object_id
WHERE t.type = 'U'
UNION ALL
SELECT 
    SCHEMA_NAME(v.schema_id) AS schema_name,
    v.name AS table_name,
    c.name AS column_name,
    TYPE_NAME(c.user_type_id) AS data_type,
    c.column_id
FROM sys.views v
INNER JOIN sys.columns c ON v.object_id = c.object_id
WHERE v.type = 'V'
ORDER BY schema_name, table_name, c.column_id;";

                DataTable columnsResult = databaseContext.QueryService.ExecuteTable(columnsQuery);

                // Build dictionary: key = "schema.table", value = list of "column_name (data_type)"
                var columnsDict = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();

                foreach (DataRow colRow in columnsResult.Rows)
                {
                    string key = $"{colRow["schema_name"]}.{colRow["table_name"]}";
                    string columnInfo = $"{colRow["column_name"]} ({colRow["data_type"]})";

                    if (!columnsDict.ContainsKey(key))
                    {
                        columnsDict[key] = new System.Collections.Generic.List<string>();
                    }
                    columnsDict[key].Add(columnInfo);
                }

                // Add Columns column
                tables.Columns.Add("Columns", typeof(string));

                foreach (DataRow row in tables.Rows)
                {
                    string schemaName = row["SchemaName"].ToString();
                    string tableName = row["TableName"].ToString();
                    string key = $"{schemaName}.{tableName}";

                    if (columnsDict.TryGetValue(key, out var columns))
                    {
                        row["Columns"] = string.Join(", ", columns);
                    }
                    else
                    {
                        row["Columns"] = "";
                    }
                }
            }


            Console.WriteLine(OutputFormatter.ConvertDataTable(tables));
            
            Logger.Success($"Retrieved {tables.Rows.Count} table(s) from [{targetDatabase}]");

            return tables;
        }
    }
}
