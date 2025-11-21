using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.Database
{
    public class Rows : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "t", LongName = "table", Required = true, Description = "Table name or FQTN (database.schema.table)")]
        private string _fqtn; // Store the full qualified table name argument

        [ArgumentMetadata(Position = 1, Description = "Maximum number of rows to retrieve (positional or use /l)")]
        private int _limit = 0; // 0 = no limit

        [ArgumentMetadata(Position = 2, ShortName = "o", LongName = "offset", Description = "Number of rows to skip (default: 0)")]
        private int _offset = 0;

        [ExcludeFromArguments]
        private string _database;
        
        [ExcludeFromArguments]
        private string _schema = null; // Let SQL Server use user's default schema
        
        [ExcludeFromArguments]
        private string _table;

        public override void ValidateArguments(string additionalArguments)
        {
            if (string.IsNullOrEmpty(additionalArguments))
            {
                throw new ArgumentException("Rows action requires at least a Table Name as an argument or a Fully Qualified Table Name (FQTN) in the format 'database.schema.table'.");
            }

            // Parse both positional and named arguments
            var (namedArgs, positionalArgs) = ParseArguments(additionalArguments);

            // Get table name from position 0 or /t: or /table:
            string tableName = GetNamedArgument(namedArgs, "t") 
                            ?? GetNamedArgument(namedArgs, "table")
                            ?? GetPositionalArgument(positionalArgs, 0);

            if (string.IsNullOrEmpty(tableName))
            {
                throw new ArgumentException("Rows action requires at least a Table Name as an argument or a Fully Qualified Table Name (FQTN) in the format 'database.schema.table'.");
            }

            _fqtn = tableName;

            // Parse the table name to extract database, schema, and table
            string[] parts = tableName.Split('.');

            if (parts.Length == 3) // Format: database.schema.table
            {
                _database = parts[0];
                _schema = parts[1];
                _table = parts[2];
            }
            else if (parts.Length == 2) // Format: schema.table
            {
                _database = null; // Use the current database
                _schema = parts[0];
                _table = parts[1];
            }
            else if (parts.Length == 1) // Format: table
            {
                _database = null; // Use the current database
                _schema = null; // Use user's default schema
                _table = parts[0];
            }
            else
            {
                throw new ArgumentException("Invalid format for the argument. Expected formats: 'database.schema.table', 'schema.table', or 'table'.");
            }

            if (string.IsNullOrEmpty(_table))
            {
                throw new ArgumentException("Table name cannot be empty.");
            }

            // Parse limit argument (position 1 or /l:)
            string limitStr = GetNamedArgument(namedArgs, "l")
                           ?? GetPositionalArgument(positionalArgs, 1);
            
            if (!string.IsNullOrEmpty(limitStr))
            {
                if (!int.TryParse(limitStr, out _limit) || _limit < 0)
                {
                    throw new ArgumentException($"Invalid limit value: {limitStr}. Limit must be a non-negative integer.");
                }
            }

            // Parse offset argument (position 2 or /o: or /offset:)
            string offsetStr = GetNamedArgument(namedArgs, "o")
                            ?? GetNamedArgument(namedArgs, "offset")
                            ?? GetPositionalArgument(positionalArgs, 2);
            
            if (!string.IsNullOrEmpty(offsetStr))
            {
                if (!int.TryParse(offsetStr, out _offset) || _offset < 0)
                {
                    throw new ArgumentException($"Invalid offset value: {offsetStr}. Offset must be a non-negative integer.");
                }
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            // Use the execution database if no database is specified
            if (string.IsNullOrEmpty(_database))
            {
                _database = databaseContext.QueryService.ExecutionDatabase;
            }

            // Build the target table name based on what was specified
            string targetTable;
            if (!string.IsNullOrEmpty(_schema))
            {
                targetTable = $"[{_database}].[{_schema}].[{_table}]";
            }
            else
            {
                // No schema specified - let SQL Server use the user's default schema
                targetTable = $"[{_database}]..[{_table}]";
            }
            
            Logger.TaskNested($"Retrieving rows from {targetTable}");
            
            if (_offset > 0 || _limit > 0)
            {
                if (_offset > 0)
                    Logger.TaskNested($"Skipping {_offset} row(s)");
                if (_limit > 0)
                    Logger.TaskNested($"Limiting to {_limit} row(s)");
            }

            // Build query with optional TOP and OFFSET/FETCH
            string query = $"SELECT";
            
            if (_limit > 0 && _offset == 0)
            {
                // Use TOP when no offset
                query += $" TOP ({_limit})";
            }
            
            query += $" * FROM {targetTable}";
            
            if (_offset > 0)
            {
                // Use OFFSET/FETCH when offset is specified
                query += " ORDER BY (SELECT NULL)"; // Dummy ORDER BY to enable OFFSET/FETCH
                query += $" OFFSET {_offset} ROWS";
                
                if (_limit > 0)
                    query += $" FETCH NEXT {_limit} ROWS ONLY";
            }

            query += ";";

            DataTable rows = databaseContext.QueryService.ExecuteTable(query);

            Console.WriteLine(OutputFormatter.ConvertDataTable(rows));

            return rows;
        }
    }
}
