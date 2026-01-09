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

        [ArgumentMetadata(Position = 1, ShortName = "l", LongName = "limit", Description = "Maximum number of rows to retrieve (default: 50)")]
        private int _limit = 50;

        [ArgumentMetadata(Position = 2, LongName = "all", Description = "Retrieve all rows without limit")]
        private bool _all = false;

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

            // Helper function to strip brackets from a part
            string StripBrackets(string part)
            {
                if (string.IsNullOrEmpty(part)) return part;
                return part.Trim('[', ']');
            }

            if (parts.Length == 3) // Format: database.schema.table
            {
                _database = StripBrackets(parts[0]);
                _schema = StripBrackets(parts[1]);
                _table = StripBrackets(parts[2]);
            }
            else if (parts.Length == 2) // Format: schema.table (SQL Server standard)
            {
                _database = null; // Use the current database
                _schema = StripBrackets(parts[0]);
                _table = StripBrackets(parts[1]);
            }
            else if (parts.Length == 1) // Format: table
            {
                _database = null; // Use the current database
                _schema = "dbo"; // Default to dbo schema
                _table = StripBrackets(parts[0]);
            }
            else
            {
                throw new ArgumentException("Invalid format. Use: [table], [schema.table], or [database.schema.table].");
            }

            if (string.IsNullOrEmpty(_table))
            {
                throw new ArgumentException("Table name cannot be empty.");
            }

            // Parse limit from named arguments or second positional argument
            string limitStr = GetNamedArgument(namedArgs, "limit", GetNamedArgument(namedArgs, "l", GetPositionalArgument(positionalArgs, 1, "50")));
            if (!int.TryParse(limitStr, out _limit))
            {
                throw new ArgumentException($"Invalid limit value: {limitStr}. Limit must be an integer.");
            }

            // Check for --all flag
            _all = namedArgs.ContainsKey("all");

            // If --all is specified, set limit to 0 (no limit)
            if (_all)
            {
                _limit = 0;
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
                _database = databaseContext.QueryService.ExecutionServer.Database;
            }

            // Build the target table name
            string targetTable = Misc.BuildQualifiedTableName(_database, _schema, _table);
            
            Logger.TaskNested($"Retrieving rows from {targetTable}");
            
            if (_limit > 0)
            {
                Logger.TaskNested($"Limiting to {_limit} row(s)");
            }
            else
            {
                Logger.TaskNested("Retrieving all rows (no limit)");
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

            Logger.Success($"Extracted {rows.Rows.Count} row(s)");

            return rows;
        }
    }
}
