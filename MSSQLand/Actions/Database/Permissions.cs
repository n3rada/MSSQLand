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

            // Build USE statement if specific database is different from current
            string useStatement = string.IsNullOrEmpty(_database) || _database == databaseContext.QueryService.ExecutionDatabase
                ? ""
                : $"USE [{_database}];";

            // Query to get permissions
            string query = $@"
            {useStatement}
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

        /// <summary>
        /// Returns a priority value for a permission. Lower values = higher importance/exploitation value.
        /// </summary>
        private int GetPermissionPriority(string permission)
        {
            // Critical server-level permissions (most dangerous)
            if (permission == "CONTROL SERVER") return 1;
            if (permission == "ALTER ANY LOGIN") return 2;
            if (permission == "ALTER ANY DATABASE") return 3;
            if (permission == "CREATE ANY DATABASE") return 4;
            
            // Administrative permissions
            if (permission == "CONTROL") return 10;
            if (permission == "TAKE OWNERSHIP") return 11;
            if (permission == "IMPERSONATE") return 12;
            if (permission == "ALTER ANY USER") return 13;
            if (permission == "ALTER ANY ROLE") return 14;
            if (permission == "ALTER ANY SCHEMA") return 15;
            
            // Code execution permissions
            if (permission == "EXECUTE") return 20;
            if (permission == "ALTER") return 21;
            if (permission == "CREATE PROCEDURE") return 22;
            if (permission == "CREATE FUNCTION") return 23;
            if (permission == "CREATE ASSEMBLY") return 24;
            
            // Data modification permissions
            if (permission == "INSERT") return 30;
            if (permission == "UPDATE") return 31;
            if (permission == "DELETE") return 32;
            
            // Data access permissions
            if (permission == "SELECT") return 40;
            if (permission == "REFERENCES") return 41;
            
            // View/metadata permissions
            if (permission == "VIEW DEFINITION") return 50;
            if (permission == "VIEW ANY DATABASE") return 51;
            if (permission == "VIEW SERVER STATE") return 52;
            if (permission == "VIEW DATABASE STATE") return 53;
            
            // Connection permissions (least critical)
            if (permission == "CONNECT") return 60;
            if (permission == "CONNECT SQL") return 61;
            
            // Default for unknown permissions
            return 100;
        }
    }
}
