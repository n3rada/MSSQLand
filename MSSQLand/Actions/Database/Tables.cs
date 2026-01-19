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

        [ArgumentMetadata(ShortName = "p", LongName = "permissions", Description = "Show permissions (slower)")]
        private bool _showPermissions = false;

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

            // Detect ConfigMgr database (CM_*) for auto-filtering collection views
            bool isConfigMgrDb = targetDatabase.StartsWith("CM_", StringComparison.OrdinalIgnoreCase);

            string filterMsg = !string.IsNullOrEmpty(_name) ? $" (name: {_name})" : "";
            string columnsMsg = _showColumns ? " with columns" : "";
            string columnMsg = !string.IsNullOrEmpty(_columnFilter) ? $" with column containing '{_columnFilter}'" : "";
            string rowsMsg = _withRows ? " (rows > 0)" : "";
            string permsMsg = _showPermissions ? " with permissions" : "";
            string collViewsMsg = isConfigMgrDb ? " (excluding collection views)" : "";
            Logger.TaskNested($"Retrieving tables from [{targetDatabase}]{filterMsg}{columnsMsg}{columnMsg}{rowsMsg}{permsMsg}{collViewsMsg}");

            // Build USE statement if specific database is provided
            string useStatement = string.IsNullOrEmpty(_database) ? "" : $"USE [{_database}];";

            // Build WHERE clause with filter (partition filter is inside OUTER APPLY)
            string whereClause = "WHERE t.type IN ('U', 'V')";
            if (!string.IsNullOrEmpty(_name))
            {
                whereClause += $" AND t.name LIKE '%{_name.Replace("'", "''")}%'";
            }

            // Exclude ConfigMgr collection views when in CM_* database (they add thousands of entries)
            if (isConfigMgrDb)
            {
                whereClause += " AND t.name NOT LIKE '_RES_COLL_%' AND t.name NOT LIKE 'v_CM_RES_COLL_%'";
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
                    t.object_id AS ObjectId,
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

            // Build object_id list for efficient filtering (avoids string concat in SQL)
            var objectIds = new System.Collections.Generic.List<string>();
            foreach (DataRow row in tables.Rows)
            {
                objectIds.Add(row["ObjectId"].ToString());
            }
            string objectIdFilter = string.Join(",", objectIds);

            // Get permissions only if requested (fn_my_permissions is expensive)
            var permissionsDict = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>>();
            
            if (_showPermissions)
            {
                string permissionsQuery = $@"{useStatement}
SELECT 
    o.object_id,
    p.permission_name 
FROM sys.objects o 
CROSS APPLY fn_my_permissions(QUOTENAME(SCHEMA_NAME(o.schema_id)) + '.' + QUOTENAME(o.name), 'OBJECT') p 
WHERE o.object_id IN ({objectIdFilter});";

                DataTable allPermissions = databaseContext.QueryService.ExecuteTable(permissionsQuery);

                foreach (DataRow permRow in allPermissions.Rows)
                {
                    string key = permRow["object_id"].ToString();
                    string permission = permRow["permission_name"].ToString();

                    if (!permissionsDict.ContainsKey(key))
                    {
                        permissionsDict[key] = new System.Collections.Generic.HashSet<string>();
                    }
                    permissionsDict[key].Add(permission);
                }
            }

            // Optionally get columns if --columns flag is set
            System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>> columnsDict = null;
            
            if (_showColumns)
            {
                string columnsQuery = $@"{useStatement}
SELECT 
    o.object_id,
    c.name AS column_name,
    TYPE_NAME(c.user_type_id) AS data_type
FROM sys.columns c
INNER JOIN sys.objects o ON c.object_id = o.object_id
WHERE o.object_id IN ({objectIdFilter})
ORDER BY o.object_id, c.column_id;";

                DataTable columnsResult = databaseContext.QueryService.ExecuteTable(columnsQuery);

                // Build dictionary: key = object_id, value = list of "column_name (data_type)"
                columnsDict = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();

                foreach (DataRow colRow in columnsResult.Rows)
                {
                    string key = colRow["object_id"].ToString();
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

            // Add a column for permissions if requested
            if (_showPermissions)
            {
                tables.Columns.Add("Permissions", typeof(string));
            }

            // Map both columns and permissions to tables
            foreach (DataRow row in tables.Rows)
            {
                string objectId = row["ObjectId"].ToString();

                // Map columns if requested
                if (_showColumns && columnsDict != null)
                {
                    if (columnsDict.TryGetValue(objectId, out var columns))
                    {
                        row["Columns"] = string.Join(", ", columns);
                    }
                    else
                    {
                        row["Columns"] = "";
                    }
                }

                // Map permissions if requested
                if (_showPermissions)
                {
                    if (permissionsDict.TryGetValue(objectId, out var permissions))
                    {
                        row["Permissions"] = string.Join(", ", permissions);
                    }
                    else
                    {
                        row["Permissions"] = "";
                    }
                }
            }

            // Remove ObjectId column before display (internal use only)
            tables.Columns.Remove("ObjectId");


            Console.WriteLine(OutputFormatter.ConvertDataTable(tables));
            
            Logger.Success($"Retrieved {tables.Rows.Count} table(s) from [{targetDatabase}]");
            
            if (!_showPermissions)
            {
                Logger.InfoNested("Use -p to show permissions");
            }

            return tables;
        }
    }
}
