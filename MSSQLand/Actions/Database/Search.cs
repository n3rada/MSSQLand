using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace MSSQLand.Actions.Database
{
    internal class Search : BaseAction
    {
        private string _database;
        private string _keyword;

        public override void ValidateArguments(string additionalArguments)
        {
            // Split the additional argument into parts (database and keywords)
            string[] parts = SplitArguments(additionalArguments);

            if (parts.Length == 1)
            {
                // Only the keyword is provided, set _database to null
                _keyword = parts[0].Trim();
                _database = null; // Use the default database in this case
            }
            else if (parts.Length == 2)
            {
                // Both database and keyword are provided
                _database = parts[0].Trim();
                _keyword = parts[1].Trim();
            }

            else
            {
                throw new ArgumentException("Invalid arguments. Search usage: database keyword or keyword");
            }

            if (string.IsNullOrEmpty(_keyword))
            {
                throw new ArgumentException("The keyword cannot be empty.");
            }

        }

        public override void Execute(DatabaseContext connectionManager)
        {
            if (string.IsNullOrEmpty(_database))
            {
                _database = connectionManager.Server.Database;
            }

            Logger.TaskNested($"Searching for '{_keyword}' in database '{_database}'");

            // Query to get all columns in all tables of the specified database
            string metadataQuery = $@"
        SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE 
        FROM [{_database}].INFORMATION_SCHEMA.COLUMNS
        WHERE DATA_TYPE IN ('char', 'nchar', 'varchar', 'nvarchar', 'text', 'ntext');";

            DataTable columnsTable = connectionManager.QueryService.ExecuteTable(metadataQuery);

            if (columnsTable.Rows.Count == 0)
            {
                Logger.Warning($"No text-based columns found in database '{_database}'.");
                return;
            }

            // Group columns by table to construct queries for the entire table
            var tableColumns = new Dictionary<string, List<string>>();

            foreach (DataRow row in columnsTable.Rows)
            {
                string schema = row["TABLE_SCHEMA"].ToString();
                string table = row["TABLE_NAME"].ToString();
                string column = row["COLUMN_NAME"].ToString();

                string tableKey = $"{schema}.{table}";
                if (!tableColumns.ContainsKey(tableKey))
                {
                    tableColumns[tableKey] = new List<string>();
                }

                tableColumns[tableKey].Add(column);
            }

            // Search for the keyword in each table
            foreach (var tableEntry in tableColumns)
            {
                string[] tableInfo = tableEntry.Key.Split('.');
                string schema = tableInfo[0];
                string table = tableInfo[1];

                // Construct WHERE clause for all text-based columns
                string whereClause = string.Join(" OR ", tableEntry.Value.Select(col => $"{col} LIKE '%{_keyword}%'"));

                string searchQuery = $@"
            SELECT * 
            FROM [{_database}].[{schema}].[{table}]
            WHERE {whereClause};";

                try
                {
                    DataTable resultTable = connectionManager.QueryService.ExecuteTable(searchQuery);

                    if (resultTable.Rows.Count > 0)
                    {
                        Logger.NewLine();
                        Logger.Success($"Found '{_keyword}' in [{_database}].[{schema}].[{table}]:");
                        Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(resultTable));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Error searching in {schema}.{table}: {ex.Message}");
                }
            }

            Logger.Success("Search completed.");
        }


    }
}
