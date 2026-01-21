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
            
            Logger.TaskNested($"Retrieving rows from {targetTable}");
            
            if (_limit > 0)
            {
                Logger.TaskNested($"Limiting to {_limit} row(s)");
                Logger.TaskNested("Use --all to retrieve all rows");
            }

            // Build column list, handling XML columns for distributed queries
            string columnList = BuildColumnList(databaseContext, database, schema, table);
            string topClause = _limit > 0 ? $"TOP ({_limit}) " : "";
            string query = $"SELECT {topClause}{columnList} FROM {targetTable};";

            DataTable rows = databaseContext.QueryService.ExecuteTable(query);

            Console.WriteLine(OutputFormatter.ConvertDataTable(rows));

            Logger.Success($"Extracted {rows.Rows.Count} row(s)");

            return rows;
        }

        /// <summary>
        /// Builds a column list for SELECT, casting XML columns to NVARCHAR(MAX) to support distributed queries.
        /// XML data types are not supported in EXEC AT or OPENQUERY.
        /// </summary>
        private string BuildColumnList(DatabaseContext databaseContext, string database, string schema, string table)
        {
            // Only need special handling for linked server queries
            if (databaseContext.QueryService.LinkedServers.IsEmpty)
            {
                return "*";
            }

            try
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
                    // Fallback to * if we can't get column info
                    return "*";
                }

                var columnExpressions = new System.Collections.Generic.List<string>();
                bool hasXmlColumns = false;

                foreach (DataRow row in columnsTable.Rows)
                {
                    string columnName = row["ColumnName"].ToString();
                    string typeName = row["TypeName"].ToString().ToLowerInvariant();

                    if (typeName == "xml")
                    {
                        // Cast XML to NVARCHAR(MAX) for distributed query compatibility
                        columnExpressions.Add($"CAST([{columnName}] AS NVARCHAR(MAX)) AS [{columnName}]");
                        hasXmlColumns = true;
                    }
                    else
                    {
                        columnExpressions.Add($"[{columnName}]");
                    }
                }

                if (hasXmlColumns)
                {
                    Logger.Warning("XML columns detected - casting to NVARCHAR(MAX) for distributed query compatibility");
                }

                return string.Join(", ", columnExpressions);
            }
            catch
            {
                // If column detection fails, fall back to * and let SQL Server error if needed
                return "*";
            }
        }
    }
}
