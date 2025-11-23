using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;
using System.Linq;

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
    /// Uses fn_my_permissions to check what the current user can do.
    /// Schema defaults to the user's default schema if not explicitly specified.
    /// </summary>
    internal class Permissions : BaseAction
    {
        [ArgumentMetadata(Position = 0, Description = "Fully Qualified Table Name (database.schema.table) or empty for your permissions")]
        private string _fqtn;

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
                // No arguments - will show server and database permissions
                return;
            }

            // Parse both positional and named arguments
            var (namedArgs, positionalArgs) = ParseActionArguments(args);

            // Get table name from position 0
            string tableName = GetPositionalArgument(positionalArgs, 0);

            if (string.IsNullOrEmpty(tableName))
            {
                throw new ArgumentException("Invalid format for the argument. Expected 'database.schema.table', 'schema.table', or nothing to return current server permissions.");
            }

            _fqtn = tableName;

            // Parse the table name to extract database, schema, and table
            string[] parts = tableName.Split('.');

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

                var serverPerms = databaseContext.QueryService.ExecuteTable("SELECT permission_name AS Permission FROM fn_my_permissions(NULL, 'SERVER');");
                var sortedServerPerms = SortPermissionsByImportance(serverPerms);
                Console.WriteLine(OutputFormatter.ConvertDataTable(sortedServerPerms));

                Logger.Info("Database permissions");

                var dbPerms = databaseContext.QueryService.ExecuteTable("SELECT permission_name AS Permission FROM fn_my_permissions(NULL, 'DATABASE');");
                var sortedDbPerms = SortPermissionsByImportance(dbPerms);
                Console.WriteLine(OutputFormatter.ConvertDataTable(sortedDbPerms));

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
            var sortedTable = SortPermissionsByImportance(dataTable);

            Console.WriteLine(OutputFormatter.ConvertDataTable(sortedTable));
            return null;
        }

        /// <summary>
        /// Sorts permissions by exploitation value - most interesting permissions first.
        /// </summary>
        private System.Data.DataTable SortPermissionsByImportance(System.Data.DataTable permissionsTable)
        {
            if (permissionsTable.Rows.Count == 0)
            {
                return permissionsTable;
            }

            var sortedRows = permissionsTable.AsEnumerable()
                .OrderBy(row => GetPermissionPriority(row["Permission"].ToString()))
                .ThenBy(row => row["Permission"].ToString());

            return sortedRows.CopyToDataTable();
        }
    }
}
