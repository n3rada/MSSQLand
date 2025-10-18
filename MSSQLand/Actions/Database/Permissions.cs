using MSSQLand.Services;
using MSSQLand.Utilities;
using System;

namespace MSSQLand.Actions.Database
{
    internal class Permissions : BaseAction
    {
        private string _database;
        private string _schema = "dbo"; // Default schema
        private string _table;

        public override void ValidateArguments(string additionalArguments)
        {
            if (string.IsNullOrEmpty(additionalArguments))
            {
                return;
            }

            string[] parts = SplitArguments(additionalArguments, ".");

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
                throw new ArgumentException("Invalid format for the argument. Expected 'database.schema.table' or 'database..table' or nothing to return current server permissions.");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            
            if (string.IsNullOrEmpty(_table))
            {
                Logger.TaskNested("Listing permissions of the current user on server and accessible databases");
                Logger.NewLine();
                Logger.Info("Server permissions");


                Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(databaseContext.QueryService.ExecuteTable("SELECT permission_name AS Permission FROM fn_my_permissions(NULL, 'SERVER');")));

                Logger.Info("Database permissions");

                Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(databaseContext.QueryService.ExecuteTable("SELECT permission_name AS Permission FROM fn_my_permissions(NULL, 'DATABASE');")));

                Logger.Info("Database access");

                Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(databaseContext.QueryService.ExecuteTable("SELECT name AS [Accessible Database] FROM sys.databases WHERE HAS_DBACCESS(name) = 1;")));

                return null;
            }

            string targetTable = $"[{_schema}].[{_table}]";
            Logger.TaskNested($"Listing permissions for {databaseContext.UserService.MappedUser} on [{_database}].{targetTable}");

            // Query to get permissions
            string query = $@"
            USE [{_database}];
            SELECT DISTINCT
                permission_name AS [Permission]
            FROM 
                fn_my_permissions('{targetTable}', 'OBJECT');
            ";

            var dataTable = databaseContext.QueryService.ExecuteTable(query);

            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(dataTable));
            return null;
        }
    }
}
