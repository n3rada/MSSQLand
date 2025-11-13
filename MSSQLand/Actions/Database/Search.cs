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
        [ArgumentMetadata(Position = 0, ShortName = "k", LongName = "keyword", Required = true, Description = "Keyword to search for, or * to search all accessible databases")]
        private string _keyword;

        [ExcludeFromArguments]
        private bool _allDatabases = false;

        public override void ValidateArguments(string additionalArguments)
        {
            if (string.IsNullOrEmpty(additionalArguments))
            {
                throw new ArgumentException("Keyword is required. Usage: /a:search <keyword> or /a:search * to search all databases");
            }

            _keyword = additionalArguments.Trim();

            // Check if searching all databases
            if (_keyword == "*")
            {
                _allDatabases = true;
            }
            else if (string.IsNullOrEmpty(_keyword))
            {
                throw new ArgumentException("The keyword cannot be empty.");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            List<string> databasesToSearch = new();

            if (_allDatabases)
            {
                Logger.Task("Searching for keyword across ALL accessible databases");
                Logger.TaskNested("Searching in accessible user tables only (excluding Microsoft system tables)");
                
                // Get all accessible databases
                DataTable accessibleDatabases = databaseContext.QueryService.ExecuteTable(
                    "SELECT name FROM master.sys.databases WHERE HAS_DBACCESS(name) = 1 AND state = 0 ORDER BY name;"
                );

                foreach (DataRow row in accessibleDatabases.Rows)
                {
                    databasesToSearch.Add(row["name"].ToString());
                }

                Logger.TaskNested($"Found {databasesToSearch.Count} accessible databases to search.");
            }
            else
            {
                Logger.Task($"Lurking for '{_keyword}' in user tables only (excluding Microsoft system tables)");
                // Use the database from /db: global parameter
                string database = databaseContext.Server.Database;
                databasesToSearch.Add(database);
            }
            
            int totalHeaderMatches = 0;
            int totalRowMatches = 0;
            int totalTablesSearched = 0;

            foreach (string dbName in databasesToSearch)
            {
                Logger.TaskNested($"Searching database: {dbName}");
                var (headerMatches, rowMatches, tablesSearched) = SearchDatabase(databaseContext, dbName);
                totalHeaderMatches += headerMatches;
                totalRowMatches += rowMatches;
                totalTablesSearched += tablesSearched;
            }

            Logger.NewLine();
            Logger.Success($"Search completed across {databasesToSearch.Count} database(s) and {totalTablesSearched} table(s):");
            Logger.TaskNested($"Column header matches: {totalHeaderMatches}");
            Logger.TaskNested($"Row matches: {totalRowMatches}");

            return null;
        }

        private (int headerMatches, int rowMatches, int tablesSearched) SearchDatabase(DatabaseContext databaseContext, string database)
        {
            // Escape single quotes in keyword for SQL
            string escapedKeyword = _keyword.Replace("'", "''");

            // Query to get all columns in all tables of the specified database
            string metadataQuery = $@"
                SELECT 
                    s.name AS TABLE_SCHEMA, 
                    t.name AS TABLE_NAME, 
                    c.name AS COLUMN_NAME, 
                    ty.name AS DATA_TYPE, 
                    c.column_id AS ORDINAL_POSITION
                FROM [{database}].sys.tables t
                INNER JOIN [{database}].sys.schemas s ON t.schema_id = s.schema_id
                INNER JOIN [{database}].sys.columns c ON t.object_id = c.object_id
                INNER JOIN [{database}].sys.types ty ON c.user_type_id = ty.user_type_id
                WHERE t.is_ms_shipped = 0
                ORDER BY s.name, t.name, c.column_id;";
            
            DataTable columnsTable;
            try
            {
                columnsTable = databaseContext.QueryService.ExecuteTable(metadataQuery);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to query metadata for database '{database}': {ex.Message}");
                return (0, 0, 0);
            }

            if (columnsTable.Rows.Count == 0)
            {
                Logger.Warning($"No user tables found in database '{database}'.");
                return (0, 0, 0);
            }

            // Group columns by table
            var tableColumns = new Dictionary<string, List<(string columnName, string dataType, int position)>>();

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
                string dataType = row["DATA_TYPE"].ToString();
                int position = Convert.ToInt32(row["ORDINAL_POSITION"]);

                string tableKey = $"{schema}.{table}";
                if (!tableColumns.ContainsKey(tableKey))
                {
                    tableColumns[tableKey] = new List<(string, string, int)>();
                }

                tableColumns[tableKey].Add((column, dataType, position));

                // Search for the keyword in column name
                if (column.IndexOf(_keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    headerMatches.Rows.Add($"[{database}].[{schema}].[{table}]", column, position);
                }
            }

            int headerMatchCount = headerMatches.Rows.Count;
            int rowMatchCount = 0;
            int tablesSearched = 0;

            // Log header matches
            if (headerMatchCount > 0)
            {
                Logger.Success($"Found {headerMatchCount} column header match(es) containing '{_keyword}':");
                Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(headerMatches));
            }

            // Search for the keyword in each table's rows
            foreach (var tableEntry in tableColumns)
            {
                string[] tableInfo = tableEntry.Key.Split('.');
                string schema = tableInfo[0];
                string table = tableInfo[1];

                // Build WHERE clause - only search text-based columns and convert others to string
                List<string> conditions = new List<string>();
                
                foreach (var (columnName, dataType, _) in tableEntry.Value)
                {
                    // Escape column names with square brackets
                    string escapedColumn = $"[{columnName}]";

                    // Handle different data types
                    if (IsTextType(dataType))
                    {
                        // Direct string comparison for text types
                        conditions.Add($"{escapedColumn} LIKE '%{escapedKeyword}%'");
                    }
                    else if (IsConvertibleType(dataType))
                    {
                        // Convert numeric/date types to string for comparison
                        conditions.Add($"CAST({escapedColumn} AS NVARCHAR(MAX)) LIKE '%{escapedKeyword}%'");
                    }
                    // Skip binary, image, and other non-searchable types
                }

                if (conditions.Count == 0)
                {
                    continue; // Skip tables with no searchable columns
                }

                string whereClause = string.Join(" OR ", conditions);

                // Get ALL matching rows without limit
                string searchQuery = $@"
                    SELECT * 
                    FROM [{database}].[{schema}].[{table}]
                    WHERE {whereClause};";

                try
                {
                    tablesSearched++;
                    
                    DataTable resultTable = databaseContext.QueryService.ExecuteTable(searchQuery);

                    if (resultTable.Rows.Count > 0)
                    {
                        rowMatchCount += resultTable.Rows.Count;
                        Logger.NewLine();
                        Logger.Success($"Found {resultTable.Rows.Count} row(s) containing '{_keyword}' in [{database}].[{schema}].[{table}]:");
                        Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(resultTable));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Failed to search table [{schema}].[{table}]: {ex.Message}");
                }
            }

            return (headerMatchCount, rowMatchCount, tablesSearched);
        }

        /// <summary>
        /// Checks if a data type is text-based and can be searched directly.
        /// </summary>
        private bool IsTextType(string dataType)
        {
            string[] textTypes = { "char", "varchar", "nchar", "nvarchar", "text", "ntext" };
            return textTypes.Any(t => dataType.Contains(t));
        }

        /// <summary>
        /// Checks if a data type can be converted to string for searching.
        /// </summary>
        private bool IsConvertibleType(string dataType)
        {
            string[] convertibleTypes = { 
                "int", "bigint", "smallint", "tinyint", "bit",
                "decimal", "numeric", "float", "real", "money", "smallmoney",
                "date", "datetime", "datetime2", "smalldatetime", "time", "datetimeoffset",
                "uniqueidentifier", "xml"
            };
            return convertibleTypes.Any(t => dataType.Contains(t));
        }
    }
}
