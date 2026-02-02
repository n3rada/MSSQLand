// MSSQLand/Actions/Database/Search.cs

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
    /// Search for keywords in column names and data.
    ///
    /// Defaults:
    /// - Scope: current database only.
    /// - Mode: data + headers.
    ///
    /// Options:
    /// - -d/--database <name>: search in specific database.
    /// - -a/--all: search across all accessible databases.
    /// - <schema.table> or <database.schema.table>: limit to one table (positional arg).
    /// - --column-name: search for keyword in column names only (fast, no row scanning).
    /// - -c/--column <pattern>: search data only in columns matching pattern.
    ///
    /// Examples:
    /// - search password                           (search 'password' in all data, current database)
    /// - search password --all                     (search in all accessible databases)
    /// - search password -d Clients                (search in specific database)
    /// - search password --column-name             (search 'password' in column names only)
    /// - search 16891057 -c %CI_ID%                (search '16891057' only in columns matching %CI_ID%)
    /// - search admin dbo.users                    (search in specific table)
    /// - search token --column-name --all          (search 'token' in column names across all DBs)
    /// </summary>
    internal class Search : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Keyword to search for")]
        private string _keyword = "";

        [ArgumentMetadata(Position = 1, Description = "Target: database name, schema.table, or database.schema.table")]
        private string _target = "";

        [ArgumentMetadata(ShortName = "d", LongName = "database", Description = "Search in specific database")]
        private string _database = "";

        [ArgumentMetadata(LongName = "column-name", Description = "Search for keyword in column names only (fast)")]
        private bool _columnsOnly = false;

        [ArgumentMetadata(ShortName = "c", LongName = "column", Description = "Filter to columns matching this pattern (e.g., -c %CI_ID%)")]
        private string _columnFilter = "";

        [ArgumentMetadata(ShortName = "a", LongName = "all", Description = "Search across all accessible databases")]
        private bool _searchAllDatabases = false;

        [ExcludeFromArguments]
        private string _limitDatabase = null;

        [ExcludeFromArguments]
        private string _targetTable = null;

        [ExcludeFromArguments]
        private string _targetSchema = null;

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);

            if (string.IsNullOrEmpty(_keyword))
            {
                throw new ArgumentException("Keyword is required. Usage: search <keyword> [target] [--all]");
            }

            // Parse target to determine what it is (database, schema.table, or database.schema.table)
            if (!string.IsNullOrEmpty(_target))
            {
                try
                {
                    var (database, schema, table) = Misc.ParseQualifiedTableName(_target);

                    if (!string.IsNullOrEmpty(database) && !string.IsNullOrEmpty(schema) && !string.IsNullOrEmpty(table))
                    {
                        // database.schema.table
                        _limitDatabase = database;
                        _targetSchema = schema;
                        _targetTable = table;
                    }
                    else if (!string.IsNullOrEmpty(schema) && !string.IsNullOrEmpty(table))
                    {
                        // schema.table (current database)
                        _targetSchema = schema;
                        _targetTable = table;
                    }
                    else if (!string.IsNullOrEmpty(table))
                    {
                        // Single identifier = database name (not table name for search)
                        _limitDatabase = table;
                    }
                }
                catch (ArgumentException)
                {
                    throw new ArgumentException("Invalid target format. Use: database, schema.table, or database.schema.table");
                }
            }

            // Handle --database flag (overrides positional target if both specify database)
            if (!string.IsNullOrEmpty(_database))
            {
                if (string.Equals(_database, "all", StringComparison.OrdinalIgnoreCase) || _database == "*")
                {
                    _searchAllDatabases = true;
                }
                else
                {
                    _limitDatabase = _database;
                }
            }
        }

        public override object Execute(DatabaseContext databaseContext)
        {
            string columnFilterMsg = string.IsNullOrEmpty(_columnFilter) ? "" : $" (column filter: {_columnFilter})";
            Logger.TaskNested($"Starting search for keyword: '{_keyword}'{columnFilterMsg}");

            // Handle column-only search
            if (_columnsOnly)
            {
                return SearchColumnsOnly(databaseContext);
            }

            // Handle specific table search
            if (!string.IsNullOrEmpty(_targetTable))
            {
                string dbName = _limitDatabase ?? databaseContext.QueryService.ExecutionServer.Database;
                Logger.TaskNested($"Looking inside {Misc.BuildQualifiedTableName(dbName, _targetSchema, _targetTable)}");
                var (headerMatches, rowMatches, _) = SearchDatabase(databaseContext, dbName, _targetSchema, _targetTable);

                Logger.Success($"Search completed");
                Logger.SuccessNested($"Column header matches: {headerMatches}");
                Logger.SuccessNested($"Row matches: {rowMatches}");
                return null;
            }

            // Handle database-wide or all databases search
            List<string> databasesToSearch = new();

            if (_searchAllDatabases)
            {
                Logger.Info("Searching across ALL accessible databases");
                DataTable accessibleDatabases = databaseContext.QueryService.ExecuteTable(
                    "SELECT name FROM master.sys.databases WHERE HAS_DBACCESS(name) = 1 AND state = 0 ORDER BY name;"
                );

                foreach (DataRow row in accessibleDatabases.Rows)
                {
                    databasesToSearch.Add(row["name"].ToString());
                }

                Logger.InfoNested($"Found {databasesToSearch.Count} accessible database(s) to search");
            }
            else if (!string.IsNullOrEmpty(_limitDatabase))
            {
                Logger.Info($"Searching in database: [{_limitDatabase}]");
                databasesToSearch.Add(_limitDatabase);
            }
            else
            {
                // Default: only current execution database
                string currentDb = databaseContext.QueryService.ExecutionServer.Database;
                Logger.Info($"Searching in current database: [{currentDb}]");
                databasesToSearch.Add(currentDb);
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

            Logger.Success($"Search completed across {databasesToSearch.Count} database(s) and {totalTablesSearched} table(s)");
            Logger.TaskNested($"Column header matches: {totalHeaderMatches}");
            Logger.TaskNested($"Row matches: {totalRowMatches}");

            return null;
        }

        /// <summary>
        /// Search only column names across all accessible databases (fast, no row data scanning).
        /// </summary>
        private object SearchColumnsOnly(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Searching for '{_keyword}' in column names only");

            List<string> databasesToSearch = new();

            if (_searchAllDatabases)
            {
                Logger.Info("Searching columns across ALL accessible databases");
                DataTable accessibleDatabases = databaseContext.QueryService.ExecuteTable(
                    "SELECT name FROM master.sys.databases WHERE HAS_DBACCESS(name) = 1 AND state = 0 ORDER BY name;"
                );

                foreach (DataRow row in accessibleDatabases.Rows)
                {
                    databasesToSearch.Add(row["name"].ToString());
                }
            }
            else if (!string.IsNullOrEmpty(_limitDatabase))
            {
                Logger.Info($"Searching columns in database: [{_limitDatabase}]");
                databasesToSearch.Add(_limitDatabase);
            }
            else
            {
                // Default: current database only
                string currentDb = databaseContext.QueryService.ExecutionServer.Database;
                Logger.Info($"Searching columns in current database: [{currentDb}]");
                databasesToSearch.Add(currentDb);
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
                    WHERE t.is_ms_shipped = 0{BuildColumnFilterCondition()}
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
                Logger.Success($"Found {totalMatches} column(s) containing '{_keyword}'");
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
                WHERE t.is_ms_shipped = 0 {tableFilter}{BuildColumnFilterCondition()}
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
                    headerMatches.Rows.Add(Misc.BuildQualifiedTableName(database, schema, table), column, position);
                }
            }

            int headerMatchCount = headerMatches.Rows.Count;
            int rowMatchCount = 0;
            int tablesSearched = 0;

            // Log header matches
            if (headerMatchCount > 0)
            {
                Logger.Success($"Found {headerMatchCount} column header match(es) containing '{_keyword}'");
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
                    FROM {Misc.BuildQualifiedTableName(database, schema, table)}
                    WHERE {whereClause};";

                try
                {
                    tablesSearched++;

                    DataTable resultTable = databaseContext.QueryService.ExecuteTable(searchQuery);

                    if (resultTable.Rows.Count > 0)
                    {
                        rowMatchCount += resultTable.Rows.Count;
                        Logger.NewLine();
                        Logger.Success($"Found {resultTable.Rows.Count} row(s) containing '{_keyword}' in {Misc.BuildQualifiedTableName(database, schema, table)}");
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
        /// Builds a SQL snippet for column name filtering when -c=<pattern> is provided.
        /// </summary>
        private string BuildColumnFilterCondition()
        {
            if (string.IsNullOrEmpty(_columnFilter))
            {
                return "";
            }

            string escapedFilter = _columnFilter.Replace("'", "''");
            return $" AND c.name LIKE '{escapedFilter}'";
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
