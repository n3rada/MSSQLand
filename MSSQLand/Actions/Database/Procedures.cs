// MSSQLand/Actions/Database/Procedures.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.Database
{
    internal class Procedures : BaseAction
    {
        private enum Mode { List, Exec, Read, Search }

        [ArgumentMetadata(Position = 0, Description = "Mode: list, exec, read, or search (default: list)")]
        private Mode _mode = Mode.List;

        [ArgumentMetadata(Position = 1, Description = "Stored procedure name as schema.procedure (required for exec/read) or search keyword (required for search)")]
        private string _procedureName = null;

        [ArgumentMetadata(Position = 2, Description = "Procedure arguments (optional for exec)")]
        private string _procedureArgs = null;

        [ExcludeFromArguments]
        private string _searchKeyword = null;

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);

            // Additional validation
            switch (_mode)
            {
                case Mode.Exec:
                    if (string.IsNullOrEmpty(_procedureName))
                    {
                        throw new ArgumentException("Missing procedure name. Example: procedures exec schema.procedure 'param1, param2'");
                    }
                    ValidateProcedureFormat(_procedureName);
                    break;

                case Mode.Read:
                    if (string.IsNullOrEmpty(_procedureName))
                    {
                        throw new ArgumentException("Missing procedure name for reading definition. Example: procedures read schema.procedure");
                    }
                    ValidateProcedureFormat(_procedureName);
                    break;

                case Mode.Search:
                    if (string.IsNullOrEmpty(_procedureName))
                    {
                        throw new ArgumentException("Missing search keyword. Example: procedures search EXEC");
                    }
                    _searchKeyword = _procedureName;
                    break;

                case Mode.List:
                    // No additional validation needed
                    break;
            }
        }

        /// <summary>
        /// Validates that procedure name is in schema.procedure format.
        /// </summary>
        private void ValidateProcedureFormat(string procedureName)
        {
            if (string.IsNullOrEmpty(procedureName))
            {
                throw new ArgumentException("Procedure name cannot be empty.");
            }

            try
            {
                var (database, schema, procedure) = Misc.ParseQualifiedTableName(procedureName);

                // We require schema.procedure (2 parts), not database.schema.procedure (3 parts)
                if (!string.IsNullOrEmpty(database))
                {
                    throw new ArgumentException($"Use 'schema.procedure' format, not database-qualified. Got: '{procedureName}'");
                }

                if (string.IsNullOrEmpty(schema) || string.IsNullOrEmpty(procedure))
                {
                    throw new ArgumentException($"Procedure name must be in 'schema.procedure' format. Got: '{procedureName}'");
                }
            }
            catch (ArgumentException)
            {
                throw new ArgumentException($"Procedure name must be in 'schema.procedure' format. Got: '{procedureName}'");
            }
        }

        public override object Execute(DatabaseContext databaseContext)
        {
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
            Logger.TaskNested($"Retrieving all stored procedures in [{databaseContext.QueryService.ExecutionServer.Database}]");

            string query = $@"
                SELECT
                    SCHEMA_NAME(p.schema_id) AS [Schema],
                    p.name AS [Name],
                    USER_NAME(OBJECTPROPERTY(p.object_id, 'OwnerId')) AS [Owner],
                    CASE
                        WHEN m.execute_as_principal_id IS NULL THEN ''
                        WHEN m.execute_as_principal_id = -2 THEN 'OWNER'
                        ELSE USER_NAME(m.execute_as_principal_id)
                    END AS [ExecuteAsContext],
                    p.create_date AS [Created],
                    p.modify_date AS [Modified]
                FROM sys.procedures p
                INNER JOIN sys.sql_modules m ON p.object_id = m.object_id;";

            DataTable procedures = databaseContext.QueryService.ExecuteTable(query);

            if (procedures.Rows.Count == 0)
            {
                Logger.Warning("No stored procedures found.");
                return procedures;
            }

            // Get all permissions in a single query
            string allPermissionsQuery = @"
                SELECT
                    SCHEMA_NAME(o.schema_id) AS schema_name,
                    o.name AS object_name,
                    p.permission_name
                FROM sys.objects o
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
                string schemaName = row["Schema"].ToString();
                string procedureName = row["Name"].ToString();
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
                    string execContext = row["ExecuteAsContext"].ToString();
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
                .ThenBy(row => row["Schema"].ToString())
                .ThenBy(row => row["Name"].ToString())
                .ThenByDescending(row => row["Modified"]);

            DataTable sortedProcedures = sortedRows.CopyToDataTable();

            Console.WriteLine(OutputFormatter.ConvertDataTable(sortedProcedures));

            Logger.Info($"Total: {sortedProcedures.Rows.Count} stored procedure(s) found");

            // Show example using the first procedure
            if (sortedProcedures.Rows.Count > 0)
            {
                string exampleSchema = sortedProcedures.Rows[0]["Schema"].ToString();
                string exampleName = sortedProcedures.Rows[0]["Name"].ToString();
                Logger.InfoNested($"Use 'procedures read {exampleSchema}.{exampleName}' to view definition");
            }

            Logger.NewLine();
            Logger.Warning("Execution context depends on the statements used inside the stored procedure.");
            Logger.WarningNested("Dynamic SQL executed with EXEC or sp_executesql runs under caller permissions by default.");
            Logger.WarningNested("Static SQL inside a procedure uses ownership chaining, which may allow operations (e.g., SELECT) that the caller is not directly permitted to perform.");

            return sortedProcedures;
        }

        /// <summary>
        /// Executes a stored procedure with optional parameters.
        /// </summary>
        private DataTable ExecuteProcedure(DatabaseContext databaseContext, string procedureName, string procedureArgs)
        {
            // Parse schema.procedure using the FQTN parser
            var (_, schema, procedure) = Misc.ParseQualifiedTableName(procedureName);

            Logger.Task($"Executing [{databaseContext.QueryService.ExecutionServer.Database}].[{schema}].[{procedure}]");
            if (!string.IsNullOrEmpty(procedureArgs))
                Logger.TaskNested($"With arguments: {procedureArgs}");

            // Use schema-qualified name in EXEC
            string query = $"EXEC [{schema}].[{procedure}] {procedureArgs};";

            try
            {
                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                Logger.Success($"Stored procedure executed successfully.");
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
        private object ReadProcedureDefinition(DatabaseContext databaseContext, string procedureName)
        {
            // Parse schema.procedure using the FQTN parser
            var (_, schema, procedure) = Misc.ParseQualifiedTableName(procedureName);

            Logger.TaskNested($"Retrieving definition of [{databaseContext.QueryService.ExecutionServer.Database}].[{schema}].[{procedure}]");

            string query = $@"
                SELECT
                    m.definition
                FROM sys.sql_modules AS m
                INNER JOIN sys.objects AS o ON m.object_id = o.object_id
                INNER JOIN sys.schemas AS s ON o.schema_id = s.schema_id
                WHERE o.type = 'P'
                AND o.name = '{procedure.Replace("'", "''")}'
                AND s.name = '{schema.Replace("'", "''")}'";

            try
            {
               DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result.Rows.Count == 0)
                {
                    Logger.Warning($"Stored procedure '{procedureName}' not found.");
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
            Logger.TaskNested($"Searching for keyword '{keyword}' in [{databaseContext.QueryService.ExecutionServer.Database}] procedures");

            string query = $@"
                SELECT
                    SCHEMA_NAME(o.schema_id) AS schema_name,
                    o.name AS procedure_name,
                    o.create_date,
                    o.modify_date
                FROM sys.sql_modules AS m
                INNER JOIN sys.objects AS o ON m.object_id = o.object_id
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