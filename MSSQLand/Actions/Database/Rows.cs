using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;

namespace MSSQLand.Actions
{
    public class Rows : BaseAction
    {
        private string _database;
        private string _schema = "dbo"; // Default schema
        private string _table;

        public override void ValidateArguments(string additionalArgument)
        {
            if (string.IsNullOrEmpty(additionalArgument))
            {
                throw new ArgumentException("Rows action requires a single argument in the format 'database.schema.table'.");
            }

            var parts = additionalArgument.Split('.');
            if (parts.Length == 2) // If no schema is provided, default to "dbo"
            {
                _database = parts[0];
                _table = parts[1];
            }
            else if (parts.Length == 3) // If schema is provided
            {
                _database = parts[0];
                _schema = parts[1];
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
            Logger.TaskNested($"Retrieving row count for {target}");

            string query = $"SELECT COUNT(*) AS RowCount FROM {target};";
            DataTable resultTable = connectionManager.QueryService.ExecuteTable(query);

            if (resultTable.Rows.Count > 0)
            {
                Console.WriteLine(resultTable.Rows[0]["RowCount"]);
            }
            else
            {
                Logger.Warning("No rows found.");
            }
        }
    }
}
