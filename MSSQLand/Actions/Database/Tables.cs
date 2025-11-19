using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;


namespace MSSQLand.Actions.Database
{
    internal class Tables : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "db", LongName = "database", Description = "Database name (uses current database if not specified)")]
        private string _database;

        public override void ValidateArguments(string additionalArguments)
        {

            _database = additionalArguments;
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            // Use the current database if no database is specified
            if (string.IsNullOrEmpty(_database))
            {
                _database = databaseContext.Server.Database;
            }

            Logger.TaskNested($"Retrieving tables from [{_database}]");


            string query = $@"
                SELECT 
                    s.name AS SchemaName,
                    t.name AS TableName,
                    t.type_desc AS TableType,
                    SUM(p.rows) AS Rows
                FROM 
                    [{_database}].sys.objects t
                JOIN 
                    [{_database}].sys.schemas s ON t.schema_id = s.schema_id
                LEFT JOIN 
                    [{_database}].sys.partitions p ON t.object_id = p.object_id
                WHERE 
                    t.type IN ('U', 'V') -- 'U' for user tables, 'V' for views
                    AND p.index_id IN (0, 1) -- 0 for heaps, 1 for clustered index
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
            string allPermissionsQuery = $@"
                USE [{_database}];
                SELECT 
                    SCHEMA_NAME(o.schema_id) AS schema_name,
                    o.name AS object_name,
                    p.permission_name
                FROM sys.objects o
                CROSS APPLY fn_my_permissions(QUOTENAME(SCHEMA_NAME(o.schema_id)) + '.' + QUOTENAME(o.name), 'OBJECT') p
                WHERE o.type IN ('U', 'V')
                ORDER BY o.name, p.permission_name;";

            DataTable allPermissions = databaseContext.QueryService.ExecuteTable(allPermissionsQuery);

            // Build a dictionary for fast lookup: key = "schema.table", value = list of permissions
            var permissionsDict = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();
            
            foreach (DataRow permRow in allPermissions.Rows)
            {
                string key = $"{permRow["schema_name"]}.{permRow["object_name"]}";
                string permission = permRow["permission_name"].ToString();

                if (!permissionsDict.ContainsKey(key))
                {
                    permissionsDict[key] = new System.Collections.Generic.List<string>();
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


            Console.WriteLine(OutputFormatter.ConvertDataTable(tables));

            return tables;
        }
    }
}
