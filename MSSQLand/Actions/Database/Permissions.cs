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

            // Define permission priority (lower = more important)
            int GetPermissionPriority(string permission)
            {
                permission = permission.ToUpper();
                
                // Tier 0: God mode
                if (permission == "CONTROL SERVER" || permission == "CONTROL") return 0;
                
                // Tier 1: Dangerous execution/impersonation
                if (permission.Contains("IMPERSONATE")) return 1;
                if (permission == "EXECUTE") return 2;
                
                // Tier 2: High-level modification (ALTER is more powerful than CREATE)
                if (permission == "ALTER ANY DATABASE" || permission == "ALTER ANY USER" || 
                    permission == "ALTER ANY ROLE" || permission == "ALTER ANY LOGIN") return 3;
                if (permission.StartsWith("ALTER ANY")) return 4;
                if (permission == "ALTER") return 5;
                if (permission.StartsWith("ALTER ")) return 6;
                
                // Tier 3: Write operations on data
                if (permission == "INSERT" || permission == "UPDATE" || permission == "DELETE") return 7;
                
                // Tier 4: Object creation
                if (permission.StartsWith("CREATE ")) return 8;
                
                // Tier 5: Read operations
                if (permission == "SELECT") return 9;
                if (permission.Contains("VIEW DEFINITION") || permission.Contains("VIEW DATABASE STATE")) return 10;
                if (permission.Contains("VIEW")) return 11;
                
                // Tier 6: Connection and basic access
                if (permission == "CONNECT" || permission == "CONNECT SQL") return 12;
                if (permission.StartsWith("CONNECT ")) return 13;
                
                // Tier 7: References and other
                if (permission == "REFERENCES") return 14;
                if (permission.Contains("BACKUP")) return 15;
                
                // Tier 8: Everything else
                return 16;
            }

            var sortedRows = permissionsTable.AsEnumerable()
                .OrderBy(row => GetPermissionPriority(row["Permission"].ToString()))
                .ThenBy(row => row["Permission"].ToString());

            return sortedRows.CopyToDataTable();
        }
    }
}
