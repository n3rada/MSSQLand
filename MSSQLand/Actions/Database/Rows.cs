using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;

namespace MSSQLand.Actions.Database
{
    public class Rows : BaseAction
    {
        private string _database;
        private string _schema = "dbo"; // Default schema
        private string _table;

        public override void ValidateArguments(string additionalArguments)
        {
            if (string.IsNullOrEmpty(additionalArguments))
            {
                throw new ArgumentException("Rows action requires at least a Table Name as an argument or a Fully Qualified Table Name (FQTN) in the format 'database.schema.table'.");
            }

            string[] parts = SplitArguments(additionalArguments, ".");

            if (parts.Length == 3) // Format: database.schema.table
            {
                _database = parts[0];
                _schema = string.IsNullOrEmpty(parts[1]) ? _schema : parts[1];
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
                _schema = "dbo"; // Default schema
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

        public override void Execute(DatabaseContext databaseContext)
        {
            // Use the current database if no database is specified
            if (string.IsNullOrEmpty(_database))
            {
                _database = databaseContext.Server.Database;
            }

            string targetTable = $"[{_database}].[{_schema}].[{_table}]";
            Logger.TaskNested($"Retrieving rows from {targetTable}");
            
            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(databaseContext.QueryService.ExecuteTable($"SELECT * FROM {targetTable};")));

        }
    }
}
