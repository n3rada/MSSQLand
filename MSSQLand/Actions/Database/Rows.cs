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
                throw new ArgumentException("Rows action requires a single argument in the format 'database.schema.table'.");
            }

            var parts = additionalArguments.Split('.');

            if (parts.Length == 3) // If schema is provided
            {
                _database = parts[0];
                
                if (!string.IsNullOrEmpty(parts[1]))
                {
                    _schema = parts[1];
                }
                _table = parts[2];
            }
            else
            {
                throw new ArgumentException("Invalid format for the argument. Expected 'database.schema.table' or 'database.table'.");
            }
        }

        public override void Execute(DatabaseContext connectionManager)
        {
            string target = $"[{_database}].[{_schema}].[{_table}]";
            Logger.TaskNested($"Retrieving rows from {target}");
            
            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(connectionManager.QueryService.ExecuteTable($"SELECT * FROM {target};")));

        }
    }
}
