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
    /// - 'all': Enumerate ALL principals' permissions for privilege escalation analysis (requires elevated permissions)
    /// - schema.table: Show permissions on a specific table in the current database
    /// - database.schema.table: Show permissions on a specific table in a specific database
    /// 
    /// Default mode uses fn_my_permissions to check what the current user can do.
    /// 
    /// 'all' mode queries sys.server_permissions and sys.database_permissions to find:
    /// - ALTER ANY LOGIN + IMPERSONATE ANY LOGIN chains
    /// - CONTROL SERVER permissions (god mode)
    /// - Targeted IMPERSONATE permissions on high-privilege accounts
    /// 
    /// Note: 'all' mode requires VIEW ANY DEFINITION or similar elevated permissions.
    /// Schema defaults to the user's default schema if not explicitly specified.
    /// </summary>
    internal class Permissions : BaseAction
    {
        [ArgumentMetadata(Position = 0, Description = "Fully Qualified Table Name (database.schema.table), 'all' for all principals, or empty for your permissions")]
        private string _fqtn;

        [ExcludeFromArguments]
        private string _database;
        
        [ExcludeFromArguments]
        private string _schema = null; // Let SQL Server use user's default schema
        
        [ExcludeFromArguments]
        private string _table;

        [ExcludeFromArguments]
        private bool ShowAllPermissions = false;

        public override void ValidateArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                // No arguments - will show server and database permissions
                return;
            }

            // Check for 'all' mode
            if (string.Join(" ", args).Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                ShowAllPermissions = true;
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
            // Handle 'all' mode - enumerate all principals' permissions
            if (ShowAllPermissions)
            {
                return EnumerateAllPermissions(databaseContext);
            }
            
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
        /// Enumerates all principals' permissions for privilege escalation analysis.
        /// Requires VIEW ANY DEFINITION or similar elevated permissions.
        /// </summary>
        private object? EnumerateAllPermissions(DatabaseContext databaseContext)
        {
            bool isAzureSQL = databaseContext.QueryService.IsAzureSQL();

            Logger.Task("Enumerating all principals' permissions for privilege escalation analysis");
            Logger.Warning("Note: Requires VIEW ANY DEFINITION or similar elevated permissions");
            Logger.NewLine();

            // Server-level permissions
            if (!isAzureSQL)
            {
                Logger.Info("Server-level permissions (instance-wide)");
                
                string serverPermsQuery = @"
                    SELECT 
                        pr.name AS [Principal],
                        pr.type_desc AS [Principal Type],
                        pe.permission_name AS [Permission],
                        pe.state_desc AS [State],
                        CASE 
                            WHEN pe.class_desc = 'SERVER' THEN 'SERVER'
                            WHEN pe.class_desc = 'SERVER_PRINCIPAL' THEN 'LOGIN: ' + ISNULL(target.name, '<deleted>')
                            WHEN pe.class_desc = 'ENDPOINT' THEN 'ENDPOINT: ' + ISNULL(ep.name, '<deleted>')
                            ELSE pe.class_desc
                        END AS [Scope]
                    FROM sys.server_permissions pe
                    INNER JOIN sys.server_principals pr ON pe.grantee_principal_id = pr.principal_id
                    LEFT JOIN sys.server_principals target ON pe.major_id = target.principal_id AND pe.class_desc = 'SERVER_PRINCIPAL'
                    LEFT JOIN sys.endpoints ep ON pe.major_id = ep.endpoint_id AND pe.class_desc = 'ENDPOINT'
                    WHERE pr.name NOT LIKE '##%'
                    AND pr.type IN ('S', 'U', 'G', 'R', 'E', 'X')
                    ORDER BY pr.name, pe.permission_name;";

                try
                {
                    DataTable serverPerms = databaseContext.QueryService.ExecuteTable(serverPermsQuery);
                    
                    if (serverPerms.Rows.Count > 0)
                    {
                        // Sort by exploitation value
                        var sortedRows = serverPerms.AsEnumerable()
                            .OrderBy(row => GetPermissionPriority(row["Permission"].ToString()))
                            .ThenBy(row => row["Principal"].ToString())
                            .ThenBy(row => row["Permission"].ToString());

                        DataTable sortedServerPerms = sortedRows.CopyToDataTable();
                        Console.WriteLine(OutputFormatter.ConvertDataTable(sortedServerPerms));
                    }
                    else
                    {
                        Logger.Warning("No server permissions found (may lack VIEW ANY DEFINITION)");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error querying server permissions: {ex.Message}");
                    Logger.WarningNested("You likely lack VIEW ANY DEFINITION or VIEW SERVER STATE permissions");
                }

                Logger.NewLine();
            }

            // Database-level permissions
            Logger.Info($"Database-level permissions ({databaseContext.QueryService.ExecutionDatabase})");
            
            string dbPermsQuery = @"
                SELECT 
                    pr.name AS [Principal],
                    pr.type_desc AS [Principal Type],
                    pe.permission_name AS [Permission],
                    pe.state_desc AS [State],
                    CASE 
                        WHEN pe.class_desc = 'DATABASE' THEN 'DATABASE'
                        WHEN pe.class_desc = 'SCHEMA' THEN 'SCHEMA: ' + ISNULL(s.name, '<deleted>')
                        WHEN pe.class_desc = 'OBJECT_OR_COLUMN' THEN 
                            ISNULL(OBJECT_SCHEMA_NAME(pe.major_id), '<deleted>') + '.' + 
                            ISNULL(OBJECT_NAME(pe.major_id), '<deleted>')
                        WHEN pe.class_desc = 'DATABASE_PRINCIPAL' THEN 'USER: ' + ISNULL(target.name, '<deleted>')
                        ELSE pe.class_desc
                    END AS [Scope]
                FROM sys.database_permissions pe
                INNER JOIN sys.database_principals pr ON pe.grantee_principal_id = pr.principal_id
                LEFT JOIN sys.schemas s ON pe.major_id = s.schema_id AND pe.class_desc = 'SCHEMA'
                LEFT JOIN sys.database_principals target ON pe.major_id = target.principal_id AND pe.class_desc = 'DATABASE_PRINCIPAL'
                WHERE pr.name NOT LIKE '##%'
                AND pr.type IN ('S', 'U', 'G', 'R', 'E', 'X', 'A')
                ORDER BY pr.name, pe.permission_name;";

            try
            {
                DataTable dbPerms = databaseContext.QueryService.ExecuteTable(dbPermsQuery);
                
                if (dbPerms.Rows.Count > 0)
                {
                    // Sort by exploitation value
                    var sortedRows = dbPerms.AsEnumerable()
                        .OrderBy(row => GetPermissionPriority(row["Permission"].ToString()))
                        .ThenBy(row => row["Principal"].ToString())
                        .ThenBy(row => row["Permission"].ToString());

                    DataTable sortedDbPerms = sortedRows.CopyToDataTable();
                    Console.WriteLine(OutputFormatter.ConvertDataTable(sortedDbPerms));
                }
                else
                {
                    Logger.Warning("No database permissions found");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error querying database permissions: {ex.Message}");
                Logger.WarningNested("You likely lack VIEW DEFINITION permissions in this database");
            }
            
            return null;
        }


        /// <summary>
        /// Gets permission priority for sorting (lower = more dangerous).
        /// </summary>
        private int GetPermissionPriority(string permission)
        {
            permission = permission.ToUpper();
            
            // Tier 0: God mode
            if (permission == "CONTROL SERVER" || permission == "CONTROL") return 0;
            
            // Tier 1: Dangerous execution/impersonation
            if (permission.Contains("IMPERSONATE")) return 1;
            if (permission == "EXECUTE") return 2;
            
            // Tier 2: High-level modification
            if (permission == "ALTER ANY DATABASE" || permission == "ALTER ANY USER" || 
                permission == "ALTER ANY ROLE" || permission == "ALTER ANY LOGIN") return 3;
            if (permission.StartsWith("ALTER ANY")) return 4;
            if (permission == "ALTER") return 5;
            if (permission.StartsWith("ALTER ")) return 6;
            
            // Tier 3: Write operations
            if (permission == "INSERT" || permission == "UPDATE" || permission == "DELETE") return 7;
            
            // Tier 4: Object creation
            if (permission.StartsWith("CREATE ")) return 8;
            
            // Tier 5: Read operations
            if (permission == "SELECT") return 9;
            if (permission.Contains("VIEW DEFINITION") || permission.Contains("VIEW DATABASE STATE")) return 10;
            if (permission.Contains("VIEW")) return 11;
            
            // Tier 6: Connection
            if (permission == "CONNECT" || permission == "CONNECT SQL") return 12;
            if (permission.StartsWith("CONNECT ")) return 13;
            
            // Tier 7: Other
            if (permission == "REFERENCES") return 14;
            if (permission.Contains("BACKUP")) return 15;
            
            return 16;
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
