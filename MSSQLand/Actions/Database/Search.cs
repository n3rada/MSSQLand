using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;

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
            SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE, ORDINAL_POSITION
            FROM [{_database}].INFORMATION_SCHEMA.COLUMNS;";

            DataTable columnsTable = connectionManager.QueryService.ExecuteTable(metadataQuery);

            if (columnsTable.Rows.Count == 0)
            {
                Logger.Warning($"No columns found in database '{_database}'.");
                return;
            }

            // Group columns by table to construct queries for the entire table
            var tableColumns = new Dictionary<string, List<string>>();

            // DataTable to store column header matches
            DataTable headerMatches = new();
            headerMatches.Columns.Add("FQTN", typeof(string));
            headerMatches.Columns.Add("Header", typeof(string));
            headerMatches.Columns.Add("Ordinal Position", typeof(int));


            foreach (DataRow row in columnsTable.Rows)
            {
                string schema = row["TABLE_SCHEMA"].ToString();
                string table = row["TABLE_NAME"].ToString();
                string column = row["COLUMN_NAME"].ToString();
                int position = Convert.ToInt32(row["ORDINAL_POSITION"]);

                string tableKey = $"{schema}.{table}";
                if (!tableColumns.ContainsKey(tableKey))
                {
                    tableColumns[tableKey] = new List<string>();
                }

                tableColumns[tableKey].Add(column);

                // Search for the keyword in column name
                if (column.IndexOf(_keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Add the match to the results
                    headerMatches.Rows.Add($"[{_database}].[{schema}].[{table}]", column, position);
                }
            }

            // Log header matches
            if (headerMatches.Rows.Count > 0)
            {
                Logger.NewLine();
                Logger.Success($"Found '{_keyword}' in column headers:");
                Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(headerMatches));
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


                DataTable resultTable = connectionManager.QueryService.ExecuteTable(searchQuery);

                if (resultTable.Rows.Count > 0)
                {
                    Logger.NewLine();
                    Logger.Success($"Found '{_keyword}' in [{_database}].[{schema}].[{table}] rows:");
                    Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(resultTable));
                }

            }

            Logger.Success("Search completed.");
        }


    }
}
