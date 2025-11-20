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

            _fqtn = additionalArguments;
            string[] parts = SplitArguments(additionalArguments, ".");

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

            DataTable rows = databaseContext.QueryService.ExecuteTable($"SELECT * FROM {targetTable};");


            Console.WriteLine(OutputFormatter.ConvertDataTable(rows));

            return rows;

        }
    }
}
