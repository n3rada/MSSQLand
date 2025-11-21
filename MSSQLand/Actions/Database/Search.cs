using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace MSSQLand.Actions.Database
{
    /// <summary>
    /// Search for keywords in column names and data across databases.
    /// 
    /// Usage:
    /// - search <keyword>: Search current database for keyword in column names and row data
    /// - search <keyword> *: Search all accessible databases
    /// - search <keyword> schema.table: Search specific table only
    /// - search <keyword> database.schema.table: Search specific table in specific database
    /// - search <keyword> --columns-only: Search column names only (no row data)
    /// 
    /// Examples:
    /// - /a:search password: Search for 'password' in current database
    /// - /a:search password *: Search for 'password' in all databases
    /// - /a:search admin dbo.users: Search only in dbo.users table
    /// - /a:search email --columns-only: Find columns containing 'email' (fast)
    /// </summary>
    internal class Search : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "k", LongName = "keyword", Required = true, Description = "Keyword to search for")]
        private string _keyword;

        [ArgumentMetadata(ShortName = "c", LongName = "columns-only", Description = "Search column names only (no row data)")]
        private bool _columnsOnly = false;

        [ArgumentMetadata(ShortName = "a", LongName = "all", Description = "Search across all accessible databases")]
        private bool _allDatabases = false;

        [ArgumentMetadata(ShortName = "t", LongName = "table", Description = "Specific table to search: schema.table or database.schema.table")]
        private string? _targetTable = null;

        [ExcludeFromArguments]
        private string? _targetDatabase = null;

        [ExcludeFromArguments]
        private string? _targetSchema = null;

        public override void ValidateArguments(string additionalArguments)
        {
            if (string.IsNullOrEmpty(additionalArguments))
            {
                throw new ArgumentException("Keyword is required. Usage: /a:search <keyword> [/c] [/a] [/t:table]");
            }
            _keyword = additionalArguments.Trim();
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            // Parse table argument if provided
            if (!string.IsNullOrEmpty(_targetTable))
            {
                string[] tableParts = _targetTable.Split('.');

                if (tableParts.Length == 3) // database.schema.table
                {
                    _targetDatabase = tableParts[0];
                    _targetSchema = tableParts[1];
                    _targetTable = tableParts[2];
                }
                else if (tableParts.Length == 2) // schema.table (current database)
                {
                    _targetSchema = tableParts[0];
                    _targetTable = tableParts[1];
                }
                else if (tableParts.Length == 1) // just table name (use dbo schema)
                {
                    _targetSchema = "dbo";
                    _targetTable = tableParts[0];
                }
                else
                {
                    throw new ArgumentException("Invalid table format. Use: schema.table or database.schema.table");
                }
            }

            // Handle column-only search
            if (_columnsOnly)
            {
                return SearchColumnsOnly(databaseContext);
            }

            // Handle specific table search
            if (!string.IsNullOrEmpty(_targetTable))
            {
                string dbName = _targetDatabase ?? databaseContext.QueryService.ExecutionDatabase;
                Logger.Task($"Searching for '{_keyword}' in [{dbName}].[{_targetSchema}].[{_targetTable}]");
                var (headerMatches, rowMatches, _) = SearchDatabase(databaseContext, dbName, _targetSchema, _targetTable);
                
                Logger.NewLine();
                Logger.Success($"Search completed:");
                Logger.TaskNested($"Column header matches: {headerMatches}");
                Logger.TaskNested($"Row matches: {rowMatches}");
                return null;
            }

            // Handle database-wide or all databases search
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
                // Use the execution database from QueryService
                string database = databaseContext.QueryService.ExecutionDatabase;
                databasesToSearch.Add(database);
            }
            
            int totalHeaderMatches = 0;
            int totalRowMatches = 0;
            int totalTablesSearched = 0;

            foreach (string dbName in databasesToSearch)
            {
                Logger.TaskNested($"Searching database: {dbName}");
                var (headerMatches, rowMatches, tablesSearched) = SearchDatabase(databaseContext, dbName, null, null);
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

        /// <summary>
        /// Search only column names across all accessible databases (fast, no row data scanning).
        /// </summary>
        private object? SearchColumnsOnly(DatabaseContext databaseContext)
        {
            Logger.Task($"Searching for '{_keyword}' in column names only (fast mode)");
            Logger.NewLine();

            // Get all accessible databases
            List<string> databasesToSearch = new();

            if (_allDatabases)
            {
                DataTable accessibleDatabases = databaseContext.QueryService.ExecuteTable(
                    "SELECT name FROM master.sys.databases WHERE HAS_DBACCESS(name) = 1 AND state = 0 ORDER BY name;"
                );

                foreach (DataRow row in accessibleDatabases.Rows)
                {
                    databasesToSearch.Add(row["name"].ToString());
                }
            }
            else
            {
                databasesToSearch.Add(databaseContext.QueryService.ExecutionDatabase);
            }

            DataTable allMatches = new();
            allMatches.Columns.Add("Database", typeof(string));
            allMatches.Columns.Add("Schema", typeof(string));
            allMatches.Columns.Add("Table", typeof(string));
            allMatches.Columns.Add("Column", typeof(string));
            allMatches.Columns.Add("Data Type", typeof(string));
            allMatches.Columns.Add("Position", typeof(int));

            int totalMatches = 0;

            foreach (string dbName in databasesToSearch)
            {
                string metadataQuery = $@"
                    SELECT 
                        '{dbName}' AS [Database],
                        s.name AS [Schema], 
                        t.name AS [Table], 
                        c.name AS [Column], 
                        ty.name AS [Data Type],
                        c.column_id AS [Position]
                    FROM [{dbName}].sys.tables t
                    INNER JOIN [{dbName}].sys.schemas s ON t.schema_id = s.schema_id
                    INNER JOIN [{dbName}].sys.columns c ON t.object_id = c.object_id
                    INNER JOIN [{dbName}].sys.types ty ON c.user_type_id = ty.user_type_id
                    WHERE t.is_ms_shipped = 0
                    AND c.name LIKE '%{_keyword.Replace("'", "''")}%'
                    ORDER BY s.name, t.name, c.column_id;";

                try
                {
                    DataTable matches = databaseContext.QueryService.ExecuteTable(metadataQuery);
                    
                    foreach (DataRow row in matches.Rows)
                    {
                        allMatches.Rows.Add(
                            row["Database"],
                            row["Schema"],
                            row["Table"],
                            row["Column"],
                            row["Data Type"],
                            row["Position"]
                        );
                    }
                    
                    totalMatches += matches.Rows.Count;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to search database '{dbName}': {ex.Message}");
                }
            }

            if (totalMatches > 0)
            {
                Logger.Success($"Found {totalMatches} column(s) containing '{_keyword}':");
                Console.WriteLine(OutputFormatter.ConvertDataTable(allMatches));
            }
            else
            {
                Logger.Warning($"No columns found containing '{_keyword}'");
            }

            return null;
        }

        private (int headerMatches, int rowMatches, int tablesSearched) SearchDatabase(DatabaseContext databaseContext, string database, string? targetSchema = null, string? targetTable = null)
        {
            // Escape single quotes in keyword for SQL
            string escapedKeyword = _keyword.Replace("'", "''");

            // Build WHERE clause for specific table if specified
            string tableFilter = "";
            if (!string.IsNullOrEmpty(targetTable))
            {
                tableFilter = $" AND s.name = '{targetSchema?.Replace("'", "''")}' AND t.name = '{targetTable.Replace("'", "''")}' ";
            }

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
                WHERE t.is_ms_shipped = 0 {tableFilter}
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
                Console.WriteLine(OutputFormatter.ConvertDataTable(headerMatches));
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
                        Console.WriteLine(OutputFormatter.ConvertDataTable(resultTable));
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
