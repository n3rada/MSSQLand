// MSSQLand/Actions/Database/Rows.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.Database
{
    public class Rows : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Table name: [table], [schema.table], or [database.schema.table]")]
        private string _fqtn = "";

        [ArgumentMetadata(Position = 1, ShortName = "l", LongName = "limit", Description = "Maximum rows to retrieve (default: 25)")]
        private int _limit = 25;

        [ArgumentMetadata(LongName = "all", Description = "Retrieve all rows without limit")]
        private bool _all = false;

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);

            if (string.IsNullOrEmpty(_fqtn))
            {
                throw new ArgumentException("Table name is required. Format: [table], [schema.table], or [database.schema.table]");
            }

            // Validate the FQTN can be parsed
            try
            {
                Misc.ParseQualifiedTableName(_fqtn);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"Invalid table name: {ex.Message}");
            }

            // If --all is specified, override limit
            if (_all)
            {
                _limit = 0;
            }

            if (_limit < 0)
            {
                throw new ArgumentException($"Limit must be a non-negative integer, got: {_limit}");
            }
        }

        public override object Execute(DatabaseContext databaseContext)
        {
            // Parse the FQTN
            var (database, schema, table) = Misc.ParseQualifiedTableName(_fqtn);

            // Use execution database if none specified
            if (string.IsNullOrEmpty(database))
            {
                database = databaseContext.QueryService.ExecutionServer.Database;
            }

            // Validate table name after parsing
            if (string.IsNullOrEmpty(table))
            {
                throw new ArgumentException("Table name cannot be empty.");
            }

            // Build the qualified table name for the query
            string targetTable = Misc.BuildQualifiedTableName(database, schema, table);

            // Get approximate row count from sys.partitions (fast metadata lookup)
            string schemaFilter = string.IsNullOrEmpty(schema) ? "dbo" : schema;
            string countQuery = $@"
SELECT SUM(p.rows)
FROM [{database}].sys.partitions p
JOIN [{database}].sys.objects o ON p.object_id = o.object_id
JOIN [{database}].sys.schemas s ON o.schema_id = s.schema_id
WHERE o.name = '{table.Replace("'", "''")}'
  AND s.name = '{schemaFilter.Replace("'", "''")}'
  AND p.index_id IN (0, 1);";

            long totalRows = 0;
            try
            {
                object result = databaseContext.QueryService.ExecuteScalar(countQuery);
                if (result != null && result != DBNull.Value)
                {
                    totalRows = Convert.ToInt64(result);
                }
            }
            catch (Exception)
            {
                // Ignore count errors, continue without row count
                Logger.Warning("Could not retrieve row count metadata.");
            }

            Logger.TaskNested($"Retrieving rows from {targetTable}");

            // Intelligently decide whether to use TOP
            bool useTop = _limit > 0 && _limit < totalRows;

            // Display appropriate message based on limit and row count
            if (_limit == 0)
            {
                // Unlimited mode
                if (totalRows > 0)
                    Logger.TaskNested($"Retrieving all {totalRows:N0} rows");
            }
            else if (totalRows == 0)
            {
                // Limited mode, no count info
                Logger.TaskNested($"Limiting to {_limit} row(s)");
                Logger.TaskNested("Use --all to retrieve all rows");
            }
            else if (useTop)
            {
                // Limited mode, applying TOP
                Logger.TaskNested($"Limiting to {_limit} row(s) over {totalRows:N0}");
                Logger.TaskNested("Use --all to retrieve all rows");
            }
            else
            {
                // Limited mode, but limit exceeds total
                Logger.TaskNested($"Retrieving all {totalRows:N0} rows (limit {_limit} exceeds total)");
            }

            string topClause = useTop ? $"TOP ({_limit}) " : "";
            DataTable rows;

            try
            {
                // Optimistic: try SELECT * first (fastest path for most tables)
                string query = $"SELECT {topClause}* FROM {targetTable};";
                rows = databaseContext.QueryService.ExecuteTable(query);
            }
            catch (System.Data.SqlClient.SqlException ex) when (ex.Number == 9514)
            {
                // Error 9514: XML data type not supported in distributed queries
                // Fall back to explicit column list with XML columns cast to NVARCHAR(MAX)
                Logger.Warning("XML columns detected - retrying with CAST to NVARCHAR(MAX)");

                string columnList = BuildColumnListWithXmlCast(databaseContext, database, schema, table);
                string query = $"SELECT {topClause}{columnList} FROM {targetTable};";
                rows = databaseContext.QueryService.ExecuteTable(query);
            }

            Console.WriteLine(OutputFormatter.ConvertDataTable(rows));

            Logger.Success($"Extracted {rows.Rows.Count} row(s)");

            return rows;
        }

        /// <summary>
        /// Builds a column list for SELECT, casting XML columns to NVARCHAR(MAX) to support distributed queries.
        /// Only called when error 9514 is encountered (XML not supported in distributed queries).
        /// </summary>
        private string BuildColumnListWithXmlCast(DatabaseContext databaseContext, string database, string schema, string table)
        {
            // Query column information using 3-part naming for linked server compatibility
            string schemaFilter = string.IsNullOrEmpty(schema) ? "dbo" : schema;
            string columnQuery = $@"
SELECT c.name AS ColumnName, t.name AS TypeName
FROM [{database}].sys.columns c
JOIN [{database}].sys.types t ON c.user_type_id = t.user_type_id
JOIN [{database}].sys.objects o ON c.object_id = o.object_id
JOIN [{database}].sys.schemas s ON o.schema_id = s.schema_id
WHERE o.name = '{table.Replace("'", "''")}'
  AND s.name = '{schemaFilter.Replace("'", "''")}'
ORDER BY c.column_id;";

            DataTable columnsTable = databaseContext.QueryService.ExecuteTable(columnQuery);

            if (columnsTable.Rows.Count == 0)
            {
                throw new InvalidOperationException($"Could not retrieve column information for table {database}.{schemaFilter}.{table}");
            }

            var columnExpressions = new System.Collections.Generic.List<string>();

            foreach (DataRow row in columnsTable.Rows)
            {
                string columnName = row["ColumnName"].ToString();
                string typeName = row["TypeName"].ToString().ToLowerInvariant();

                if (typeName == "xml")
                {
                    // Cast XML to NVARCHAR(MAX) for distributed query compatibility
                    columnExpressions.Add($"CAST([{columnName}] AS NVARCHAR(MAX)) AS [{columnName}]");
                }
                else
                {
                    columnExpressions.Add($"[{columnName}]");
                }
            }

            return string.Join(", ", columnExpressions);
        }
    }
}
