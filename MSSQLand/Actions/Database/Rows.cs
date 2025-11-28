using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.Database
{
    public class Rows : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Table name in format: [table], [schema.table], or [database.schema.table]")]
        private string _fqtn; // Store the full qualified table name argument

        [ArgumentMetadata(ShortName = "l", LongName = "limit", Description = "Maximum number of rows to retrieve (default: no limit)")]
        private int _limit = 0; // 0 = no limit

        [ExcludeFromArguments]
        private string _database;
        
        [ExcludeFromArguments]
        private string _schema = null; // Let SQL Server use user's default schema
        
        [ExcludeFromArguments]
        private string _table;

        public override void ValidateArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                throw new ArgumentException("Rows action requires at least a Table Name as an argument or a Fully Qualified Table Name (FQTN) in the format 'database.schema.table'.");
            }

            // Parse arguments using the base class method
            var (namedArgs, positionalArgs) = ParseActionArguments(args);

            // Get the FQTN from the first positional argument
            _fqtn = GetPositionalArgument(positionalArgs, 0);

            if (string.IsNullOrEmpty(_fqtn))
            {
                throw new ArgumentException("Rows action requires at least a Table Name as an argument or a Fully Qualified Table Name (FQTN) in the format 'database.schema.table'.");
            }

            // Parse the table name to extract database, schema, and table
            string[] parts = _fqtn.Split('.');

            if (parts.Length == 3) // Format: database.schema.table
            {
                _database = parts[0];
                _schema = parts[1];
                _table = parts[2];
            }
            else if (parts.Length == 2) // Format: schema.table (SQL Server standard)
            {
                _database = null; // Use the current database
                _schema = parts[0];
                _table = parts[1];
            }
            else if (parts.Length == 1) // Format: table
            {
                _database = null; // Use the current database
                _schema = "dbo"; // Default to dbo schema
                _table = parts[0];
            }
            else
            {
                throw new ArgumentException("Invalid format. Use: [table], [schema.table], or [database.schema.table].");
            }

            if (string.IsNullOrEmpty(_table))
            {
                throw new ArgumentException("Table name cannot be empty.");
            }

            // Parse limit from named arguments
            string limitStr = GetNamedArgument(namedArgs, "limit", GetNamedArgument(namedArgs, "l", "0"));
            if (!int.TryParse(limitStr, out _limit))
            {
                throw new ArgumentException($"Invalid limit value: {limitStr}. Limit must be an integer.");
            }

            // Validate limit
            if (_limit < 0)
            {
                throw new ArgumentException($"Invalid limit value: {_limit}. Limit must be a non-negative integer.");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            // Use the execution database if no database is specified
            if (string.IsNullOrEmpty(_database))
            {
                _database = databaseContext.QueryService.ExecutionDatabase;
            }

            // Build the target table name
            string targetTable = $"[{_database}].[{_schema}].[{_table}]";
            
            Logger.TaskNested($"Retrieving rows from {targetTable}");
            
            if (_limit > 0)
            {
                Logger.TaskNested($"Limiting to {_limit} row(s)");
            }

            // Build query with optional TOP
            string query = $"SELECT";
            
            if (_limit > 0)
            {
                query += $" TOP ({_limit})";
            }
            
            query += $" * FROM {targetTable};";

            DataTable rows = databaseContext.QueryService.ExecuteTable(query);

            Console.WriteLine(OutputFormatter.ConvertDataTable(rows));

            return rows;
        }
    }
}
