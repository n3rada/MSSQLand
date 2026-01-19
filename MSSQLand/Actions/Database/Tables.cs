// MSSQLand/Actions/Database/Tables.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;


namespace MSSQLand.Actions.Database
{
    internal class Tables : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "D", LongName = "database", Description = "Database name (uses current database if not specified)")]
        private string _database = "";

        [ArgumentMetadata(Position = 1, ShortName = "n", LongName = "name", Description = "Filter tables by name pattern (supports wildcards %)")]
        private string _name = "";

        [ArgumentMetadata(Position = 2, ShortName = "C", LongName = "columns", Description = "Show column names for each table")]
        private bool _showColumns = false;

        [ArgumentMetadata(ShortName = "c", LongName = "column", Description = "Filter tables containing a column name pattern (supports wildcards %)")]
        private string _columnFilter = "";

        [ArgumentMetadata(ShortName = "r", LongName = "rows", Description = "Filter out tables with 0 rows")]
        private bool _withRows = false;

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);

            // If a column filter is provided, automatically show columns
            if (!string.IsNullOrEmpty(_columnFilter))
            {
                _showColumns = true;
            }
        }

        public override object Execute(DatabaseContext databaseContext)
        {
            // Use the execution database if no database is specified
            string targetDatabase = string.IsNullOrEmpty(_database) 
                ? databaseContext.QueryService.ExecutionServer.Database 
                : _database;

            // Ensure we have a valid database name
            if (string.IsNullOrEmpty(targetDatabase))
            {
                throw new InvalidOperationException("Unable to determine target database. Please specify a database name explicitly.");
            }

            string filterMsg = !string.IsNullOrEmpty(_name) ? $" (name: {_name})" : "";
            string columnsMsg = _showColumns ? " with columns" : "";
            string columnMsg = !string.IsNullOrEmpty(_columnFilter) ? $" with column containing '{_columnFilter}'" : "";
            string rowsMsg = _withRows ? " (rows > 0)" : "";
            Logger.TaskNested($"Retrieving tables from [{targetDatabase}]{filterMsg}{columnsMsg}{columnMsg}{rowsMsg}");

            // Build USE statement if specific database is provided
            string useStatement = string.IsNullOrEmpty(_database) ? "" : $"USE [{_database}];";

            // Build WHERE clause with filter (partition filter is inside OUTER APPLY)
            string whereClause = "WHERE t.type IN ('U', 'V')";
            if (!string.IsNullOrEmpty(_name))
            {
                whereClause += $" AND t.name LIKE '%{_name.Replace("'", "''")}%'";
            }

            if (!string.IsNullOrEmpty(_columnFilter))
            {
                whereClause += $" AND EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = t.object_id AND c.name LIKE '%{_columnFilter.Replace("'", "''")}%')";
            }

            if (_withRows)
            {
                // Only filter tables (type='U') with 0 rows; always show views (type='V') since we can't count their rows
                whereClause += " AND (t.type = 'V' OR EXISTS (SELECT 1 FROM sys.partitions p WHERE p.object_id = t.object_id AND p.index_id IN (0, 1) AND p.rows > 0))";
            }

            string query = $@"
                {useStatement}
                SELECT 
                    s.name AS SchemaName,
                    t.name AS TableName,
                    t.type_desc AS TableType,
                    CASE 
                        WHEN t.type = 'U' THEN CAST(COALESCE(pr.Rows, 0) AS VARCHAR(20))
                        ELSE 'N/A'
                    END AS Rows
                FROM 
                    sys.objects t
                JOIN 
                    sys.schemas s ON t.schema_id = s.schema_id
                OUTER APPLY (
                    SELECT SUM(p.rows) AS Rows
                    FROM sys.partitions p
                    WHERE p.object_id = t.object_id AND p.index_id IN (0, 1)
                ) pr
                {whereClause}
                ORDER BY 
                    CASE WHEN t.type = 'U' THEN COALESCE(pr.Rows, 0) ELSE -1 END DESC, SchemaName, TableName;";

            DataTable tables = databaseContext.QueryService.ExecuteTable(query);

            if (tables.Rows.Count == 0)
            {
                Logger.Warning("No tables found.");
                return tables;
            }

            // Build filter for matched tables only (for permissions and columns queries)
            var tableFilter = new System.Text.StringBuilder();
            bool first = true;
            foreach (DataRow row in tables.Rows)
            {
                if (!first) tableFilter.Append(",");
                first = false;
                tableFilter.Append($"N'{row["SchemaName"]}.{row["TableName"]}'");
            }

            // Get permissions only for matched tables (fn_my_permissions is expensive)
            string permissionsQuery = $@"{useStatement}
SELECT 
    SCHEMA_NAME(o.schema_id) AS schema_name, 
    o.name AS object_name, 
    p.permission_name 
FROM sys.objects o 
CROSS APPLY fn_my_permissions(QUOTENAME(SCHEMA_NAME(o.schema_id)) + '.' + QUOTENAME(o.name), 'OBJECT') p 
WHERE o.type IN ('U', 'V') 
    AND SCHEMA_NAME(o.schema_id) + '.' + o.name IN ({tableFilter})
ORDER BY o.name, p.permission_name;";

            DataTable allPermissions = databaseContext.QueryService.ExecuteTable(permissionsQuery);

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

            // Optionally get columns if --columns flag is set
            System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>> columnsDict = null;
            
            if (_showColumns)
            {
                // Reuse tableFilter from permissions query
                string columnsQuery = $@"{useStatement}
SELECT 
    SCHEMA_NAME(o.schema_id) AS schema_name,
    o.name AS table_name,
    c.name AS column_name,
    TYPE_NAME(c.user_type_id) AS data_type,
    c.column_id
FROM sys.columns c
INNER JOIN sys.objects o ON c.object_id = o.object_id
WHERE o.type IN ('U', 'V')
    AND SCHEMA_NAME(o.schema_id) + '.' + o.name IN ({tableFilter})
ORDER BY o.name, c.column_id;";

                DataTable columnsResult = databaseContext.QueryService.ExecuteTable(columnsQuery);

                // Build dictionary: key = "schema.table", value = list of "column_name (data_type)"
                columnsDict = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();

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

                // Add Columns column first (will appear before Permissions)
                tables.Columns.Add("Columns", typeof(string));
            }

            // Add a column for permissions
            tables.Columns.Add("Permissions", typeof(string));

            // Map both columns and permissions to tables
            foreach (DataRow row in tables.Rows)
            {
                string schemaName = row["SchemaName"].ToString();
                string tableName = row["TableName"].ToString();
                string key = $"{schemaName}.{tableName}";

                // Map columns if requested
                if (_showColumns && columnsDict != null)
                {
                    if (columnsDict.TryGetValue(key, out var columns))
                    {
                        row["Columns"] = string.Join(", ", columns);
                    }
                    else
                    {
                        row["Columns"] = "";
                    }
                }

                // Map permissions
                if (permissionsDict.TryGetValue(key, out var permissions))
                {
                    row["Permissions"] = string.Join(", ", permissions);
                }
                else
                {
                    row["Permissions"] = "";
                }
            }


            Console.WriteLine(OutputFormatter.ConvertDataTable(tables));
            
            Logger.Success($"Retrieved {tables.Rows.Count} table(s) from [{targetDatabase}]");

            return tables;
        }
    }
}
