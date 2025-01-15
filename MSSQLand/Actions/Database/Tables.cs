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

        public override void Execute(DatabaseContext connectionManager)
        {
            // Use the current database if no database is specified
            if (string.IsNullOrEmpty(_database))
            {
                _database = connectionManager.Server.Database;
            }

            Logger.TaskNested($"Retrieving tables from {_database}");


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

            DataTable resultTable = connectionManager.QueryService.ExecuteTable(query);

            if (resultTable.Rows.Count == 0)
            {
                Console.WriteLine("No tables");
            }
            else
            {
                Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(resultTable));
            }

        }
    }
}
