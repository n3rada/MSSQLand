using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;


namespace MSSQLand.Actions.Database
{
    internal class Tables : BaseAction
    {
        private string _database;

        public override void ValidateArguments(string additionalArguments)
        {

            _database = additionalArguments;
        }

        public override void Execute(DatabaseContext databaseContext)
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

            DataTable resultTable = databaseContext.QueryService.ExecuteTable(query);

            // Add a column for permissions
            resultTable.Columns.Add("Permissions", typeof(string));

            foreach (DataRow row in resultTable.Rows)
            {
                string schemaName = row["SchemaName"].ToString();
                string tableName = row["TableName"].ToString();


                // Query to get user permissions on the table

                // Query to get permissions
                string permissionQuery = $@"
                USE [{_database}];
                SELECT DISTINCT
                    permission_name
                FROM 
                    fn_my_permissions('[{schemaName}].[{tableName}]', 'OBJECT');
                ";

                DataTable permissionResult = databaseContext.QueryService.ExecuteTable(permissionQuery);

                // Concatenate permissions as a comma-separated string
                string permissions = string.Join(", ", permissionResult.AsEnumerable()
                    .Select(r => r["permission_name"].ToString()));

                // Add permissions to the result row
                row["Permissions"] = permissions;

            }


            if (resultTable.Rows.Count > 0)
            {
                Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(resultTable));
            }
        }
    }
}
