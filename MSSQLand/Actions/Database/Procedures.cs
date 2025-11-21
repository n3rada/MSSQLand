using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Collections.Generic;
using System.Data;

namespace MSSQLand.Actions.Database
{
    internal class Procedures : BaseAction
    {
        private enum Mode { List, Exec, Read, Search, Sqli }
        
        [ArgumentMetadata(Position = 0, Description = "Mode: list, exec, read, search, or sqli (default: list)")]
        private Mode _mode = Mode.List;
        
        [ArgumentMetadata(Position = 1, Description = "Stored procedure name as schema.procedure (required for exec/read) or search keyword (required for search)")]
        private string? _procedureName;
        
        [ArgumentMetadata(Position = 2, Description = "Procedure arguments (optional for exec)")]
        private string? _procedureArgs;
        
        [ArgumentMetadata(ShortName = "d", LongName = "database", Description = "Database name (optional, default: current database)")]
        private string? _targetDatabase;
        
        [ExcludeFromArguments]
        private string? _searchKeyword;

        public override void ValidateArguments(string additionalArguments)
        {
            string[] parts = SplitArguments(additionalArguments);

            if (parts.Length == 0)
            {
                return; // Default to listing stored procedures
            }

            string command = parts[0].ToLower();
            switch (command)
            {
                case "list":
                    _mode = Mode.List;
                    break;

                case "exec":
                    if (parts.Length < 2)
                    {
                        throw new ArgumentException("Missing procedure name. Example: /a:procedures exec dbo.sp_GetUsers 'param1, param2'");
                    }
                    _mode = Mode.Exec;
                    _procedureName = parts[1];
                    ValidateProcedureFormat(_procedureName);
                    _procedureArgs = parts.Length > 2 ? parts[2] : "";  // Store arguments if provided
                    break;

                case "read":
                    if (parts.Length < 2)
                    {
                        throw new ArgumentException("Missing procedure name for reading definition. Example: /a:procedures read dbo.sp_GetUsers");
                    }
                    _mode = Mode.Read;
                    _procedureName = parts[1];
                    ValidateProcedureFormat(_procedureName);
                    break;

                case "search":
                    if (parts.Length < 2)
                    {
                        throw new ArgumentException("Missing search keyword. Example: /a:procedures search EXEC");
                    }
                    _mode = Mode.Search;
                    _searchKeyword = parts[1];
                    break;

                case "sqli":
                    _mode = Mode.Sqli;
                    break;

                default:
                    throw new ArgumentException("Invalid mode. Use 'list', 'exec <StoredProcedureName>', 'read <StoredProcedureName>', 'search <keyword>', or 'sqli'");
            }
        }

        /// <summary>
        /// Validates that procedure name is in schema.procedure format.
        /// </summary>
        private void ValidateProcedureFormat(string procedureName)
        {
            if (string.IsNullOrEmpty(procedureName) || !procedureName.Contains("."))
            {
                throw new ArgumentException($"Procedure name must be in 'schema.procedure' format. Got: '{procedureName}'");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.Warning("Execution context: stored procedures run under CALLER by default (unless created WITH EXECUTE AS).");
            Logger.WarningNested("https://learn.microsoft.com/en-us/sql/t-sql/language-elements/execute-transact-sql");

            switch (_mode)
            {
                case Mode.List:
                    return ListProcedures(databaseContext);
                case Mode.Exec:
                    return ExecuteProcedure(databaseContext, _procedureName, _procedureArgs);
                case Mode.Read:
                    return ReadProcedureDefinition(databaseContext, _procedureName);
                case Mode.Search:
                    return SearchProcedures(databaseContext, _searchKeyword);
                default:
                    Logger.Error("Unknown execution mode.");
                    return null;
            }
        }

        /// <summary>
        /// Lists all stored procedures in the database.
        /// </summary>
        private DataTable ListProcedures(DatabaseContext databaseContext)
        {
            string targetDb = _targetDatabase ?? databaseContext.QueryService.ExecutionDatabase;
            Logger.NewLine();
            Logger.Task($"Retrieving all stored procedures in [{targetDb}]");

            string query = $@"
                SELECT 
                    SCHEMA_NAME(p.schema_id) AS schema_name,
                    p.name AS procedure_name,
                    USER_NAME(OBJECTPROPERTY(p.object_id, 'OwnerId')) AS owner,
                    CASE 
                        WHEN m.execute_as_principal_id IS NULL THEN 'CALLER'
                        WHEN m.execute_as_principal_id = -2 THEN 'OWNER'
                        ELSE USER_NAME(m.execute_as_principal_id)
                    END AS execute_as,
                    p.create_date,
                    p.modify_date
                FROM [{targetDb}].sys.procedures p
                INNER JOIN [{targetDb}].sys.sql_modules m ON p.object_id = m.object_id;";

            DataTable procedures = databaseContext.QueryService.ExecuteTable(query);

            if (procedures.Rows.Count == 0)
            {
                Logger.Warning("No stored procedures found.");
                return procedures;
            }

            // Get all permissions in a single query
            string allPermissionsQuery = $@"
                SELECT 
                    SCHEMA_NAME(o.schema_id) AS schema_name,
                    o.name AS object_name,
                    p.permission_name
                FROM [{targetDb}].sys.objects o
                CROSS APPLY fn_my_permissions(QUOTENAME(SCHEMA_NAME(o.schema_id)) + '.' + QUOTENAME(o.name), 'OBJECT') p
                WHERE o.type = 'P'
                ORDER BY o.name, p.permission_name;";

            DataTable allPermissions = databaseContext.QueryService.ExecuteTable(allPermissionsQuery);

            // Build a dictionary for fast lookup: key = "schema.procedure", value = list of permissions
            var permissionsDict = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();
            
            foreach (DataRow permRow in allPermissions.Rows)
            {
                string key = $"{permRow["schema_name"]}.{permRow["object_name"]}";
                string permission = permRow["permission_name"].ToString();

                if (!permissionsDict.ContainsKey(key))
                {
                    permissionsDict[key] = new System.Collections.Generic.List<string>();
                }
                permissionsDict[key].Add(permission);
            }

            // Add a column for permissions
            procedures.Columns.Add("Permissions", typeof(string));

            // Map permissions to procedures
            foreach (DataRow row in procedures.Rows)
            {
                string schemaName = row["schema_name"].ToString();
                string procedureName = row["procedure_name"].ToString();
                string key = $"{schemaName}.{procedureName}";

                if (permissionsDict.TryGetValue(key, out var permissions))
                {
                    row["Permissions"] = string.Join(", ", permissions);
                }
                else
                {
                    row["Permissions"] = "";
                }
            }

            // Sort in C#: execution context, then permissions, then schema/name
            var sortedRows = procedures.AsEnumerable()
                .OrderBy(row => 
                {
                    string execContext = row["execution_context"].ToString();
                    return (execContext == "CALLER" || execContext == "OWNER") ? 1 : 0;
                })
                .ThenBy(row =>
                {
                    string perms = row["Permissions"].ToString();
                    if (perms.Contains("EXECUTE")) return 0;
                    if (perms.Contains("CONTROL")) return 1;
                    if (perms.Contains("ALTER")) return 2;
                    return 3;
                })
                .ThenBy(row => row["schema_name"].ToString())
                .ThenBy(row => row["procedure_name"].ToString())
                .ThenByDescending(row => row["modify_date"]);

            DataTable sortedProcedures = sortedRows.CopyToDataTable();

            Console.WriteLine(OutputFormatter.ConvertDataTable(sortedProcedures));

            Logger.NewLine();
            Logger.Info($"Total: {sortedProcedures.Rows.Count} stored procedure(s) found");

            return sortedProcedures;
        }

        /// <summary>
        /// Executes a stored procedure with optional parameters.
        /// </summary>
        private DataTable ExecuteProcedure(DatabaseContext databaseContext, string procedureName, string procedureArgs)
        {
            string targetDb = _targetDatabase ?? databaseContext.QueryService.ExecutionDatabase;
            Logger.Task($"Executing [{targetDb}].[{procedureName}]");
            if (!string.IsNullOrEmpty(procedureArgs))
                Logger.TaskNested($"With arguments: {procedureArgs}");

            // Use fully qualified name with database context
            string query = $"USE [{targetDb}]; EXEC [{procedureName.Replace(".", "].[")}] {procedureArgs};";

            try
            {
                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                Logger.Success($"Stored procedure executed successfully.");
                Logger.NewLine();
                Console.WriteLine(OutputFormatter.ConvertDataTable(result));
                return result;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error executing stored procedure: {ex.Message}");
                return new DataTable();  // Return an empty table to prevent crashes
            }
        }

        /// <summary>
        /// Reads the definition of a stored procedure.
        /// </summary>
        private object? ReadProcedureDefinition(DatabaseContext databaseContext, string procedureName)
        {
            string targetDb = _targetDatabase ?? databaseContext.QueryService.ExecutionDatabase;
            Logger.NewLine();
            Logger.Task($"Retrieving definition of [{targetDb}].[{procedureName}]");

            // Parse schema.procedure format
            string[] parts = procedureName.Split('.');
            string schema = parts[0];
            string procedure = parts[1];

            string query = $@"
                SELECT 
                    m.definition
                FROM [{targetDb}].sys.sql_modules AS m
                INNER JOIN [{targetDb}].sys.objects AS o ON m.object_id = o.object_id
                INNER JOIN [{targetDb}].sys.schemas AS s ON o.schema_id = s.schema_id
                WHERE o.type = 'P' 
                AND o.name = '{procedure.Replace("'", "''")}'
                AND s.name = '{schema.Replace("'", "''")}'";

            try
            {
               DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result.Rows.Count == 0)
                {
                    Logger.Warning($"Stored procedure '[{targetDb}].[{procedureName}]' not found.");
                    return null;
                }

                string definition = result.Rows[0]["definition"].ToString();
                Logger.Success($"Stored procedure definition retrieved.");
                Console.WriteLine($"\n```sql\n{definition}\n```\n");

                return definition;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error retrieving stored procedure definition: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Searches for stored procedures containing a specific keyword in their definition.
        /// </summary>
        private DataTable SearchProcedures(DatabaseContext databaseContext, string keyword)
        {
            string targetDb = _targetDatabase ?? databaseContext.QueryService.ExecutionDatabase;
            Logger.NewLine();
            Logger.Info($"Searching for keyword '{keyword}' in [{targetDb}] procedures");

            string query = $@"
                SELECT 
                    SCHEMA_NAME(o.schema_id) AS schema_name,
                    o.name AS procedure_name,
                    o.create_date,
                    o.modify_date
                FROM [{targetDb}].sys.sql_modules AS m
                INNER JOIN [{targetDb}].sys.objects AS o ON m.object_id = o.object_id
                WHERE o.type = 'P' 
                AND m.definition LIKE '%{keyword.Replace("'", "''")}%'
                ORDER BY o.modify_date DESC;";

            try
            {
                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result.Rows.Count == 0)
                {
                    Logger.Warning($"No stored procedures found containing keyword '{keyword}'.");
                    return result;
                }

                Logger.Success($"Found {result.Rows.Count} stored procedure(s) containing '{keyword}'.");
                Console.WriteLine(OutputFormatter.ConvertDataTable(result));

                Logger.NewLine();
                Logger.Info($"Total: {result.Rows.Count} stored procedure(s) matching search criteria");

                return result;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error searching stored procedures: {ex.Message}");
                return new DataTable();
            }
        }
    }
}