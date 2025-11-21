using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;

namespace MSSQLand.Actions.Database
{
    /// <summary>
    /// Enumerate user and role permissions at server, database, and object levels.
    /// 
    /// Usage:
    /// - No arguments: Show current user's server, database, and database access permissions
    /// - schema.table: Show permissions on a specific table in the current database
    /// - database.schema.table: Show permissions on a specific table in a specific database
    /// 
    /// This action uses fn_my_permissions to check what the current user can do on:
    /// - Server-level objects (when called with no arguments)
    /// - Database-level objects (when called with no arguments)
    /// - Specific tables (when table name is provided)
    /// 
    /// Note: Schema defaults to the user's default schema if not explicitly specified.
    /// </summary>
    internal class Permissions : BaseAction
    {
        [ArgumentMetadata(Position = 0, Description = "Fully Qualified Table Name (database.schema.table) or empty for server/database permissions")]
        private string _fqtn;

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
                // No arguments - will show server and database permissions
                return;
            }

            _fqtn = additionalArguments;
            string[] parts = SplitArguments(additionalArguments, ".");

            if (parts.Length == 3) // Format: database.schema.table
            {
                _database = parts[0];
                _schema = parts[1]; // Use explicitly specified schema
                _table = parts[2];
            }
            else if (parts.Length == 2) // Format: schema.table (current database)
            {
                _database = null; // Use current database
                _schema = parts[0]; // Use explicitly specified schema
                _table = parts[1];
            }
            else
            {
                throw new ArgumentException("Invalid format for the argument. Expected 'database.schema.table', 'schema.table', or nothing to return current server permissions.");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            
            if (string.IsNullOrEmpty(_table))
            {
                Logger.TaskNested("Listing permissions of the current user on server and accessible databases");
                Logger.NewLine();
                Logger.Info("Server permissions");

                Console.WriteLine(OutputFormatter.ConvertDataTable(databaseContext.QueryService.ExecuteTable("SELECT permission_name AS Permission FROM fn_my_permissions(NULL, 'SERVER');")));

                Logger.Info("Database permissions");

                Console.WriteLine(OutputFormatter.ConvertDataTable(databaseContext.QueryService.ExecuteTable("SELECT permission_name AS Permission FROM fn_my_permissions(NULL, 'DATABASE');")));

                Logger.Info("Database access");

                Console.WriteLine(OutputFormatter.ConvertDataTable(databaseContext.QueryService.ExecuteTable("SELECT name AS [Accessible Database] FROM master.sys.databases WHERE HAS_DBACCESS(name) = 1;")));

                return null;
            }

            // Use the execution database if no database is specified
            if (string.IsNullOrEmpty(_database))
            {
                _database = databaseContext.QueryService.ExecutionDatabase;
            }

            // Build the target table name based on what was specified
            string targetTable;
            if (!string.IsNullOrEmpty(_schema))
            {
                targetTable = $"[{_schema}].[{_table}]";
            }
            else
            {
                // No schema specified - let SQL Server use the user's default schema
                targetTable = $"..[{_table}]";
            }

            Logger.TaskNested($"Listing permissions for {databaseContext.UserService.MappedUser} on [{_database}]{targetTable}");

            // Query to get permissions
            string query = $@"
            USE [{_database}];
            SELECT DISTINCT
                permission_name AS [Permission]
            FROM 
                fn_my_permissions('{targetTable}', 'OBJECT');
            ";

            var dataTable = databaseContext.QueryService.ExecuteTable(query);

            Console.WriteLine(OutputFormatter.ConvertDataTable(dataTable));
            return null;
        }
    }
}
