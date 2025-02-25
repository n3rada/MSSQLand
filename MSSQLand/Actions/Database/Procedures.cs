using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;

namespace MSSQLand.Actions.Database
{
    internal class Procedures : BaseAction
    {
        private enum Mode { List, Exec, Read }
        private Mode _mode = Mode.List;
        private string? _procedureName;
        private string? _procedureArgs;

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
                        throw new ArgumentException("Missing procedure name. Example: /a:procedures exec sp_GetUsers 'param1, param2'");
                    }
                    _mode = Mode.Exec;
                    _procedureName = parts[1];
                    _procedureArgs = parts.Length > 2 ? parts[2] : "";  // Store arguments if provided
                    break;

                case "read":
                    if (parts.Length < 2)
                    {
                        throw new ArgumentException("Missing procedure name for reading definition.");
                    }
                    _mode = Mode.Read;
                    _procedureName = parts[1];
                    break;

                default:
                    throw new ArgumentException("Invalid mode. Use 'list', 'exec <StoredProcedureName>', or 'read <StoredProcedureName> <Args>'");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            switch (_mode)
            {
                case Mode.List:
                    return ListProcedures(databaseContext);
                case Mode.Exec:
                    return ExecuteProcedure(databaseContext, _procedureName, _procedureArgs);
                case Mode.Read:
                    return ReadProcedureDefinition(databaseContext, _procedureName);
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
            Logger.NewLine();
            Logger.Info("Retrieving all stored procedures in the database...");

            string query = @"
                SELECT 
                    SCHEMA_NAME(schema_id) AS schema_name,
                    name AS procedure_name,
                    create_date,
                    modify_date
                FROM sys.procedures
                ORDER BY modify_date DESC;";

            DataTable procedures = databaseContext.QueryService.ExecuteTable(query);

            if (procedures.Rows.Count == 0)
            {
                Logger.Warning("No stored procedures found.");
                return procedures;
            }

            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(procedures));

            return procedures;
        }

        /// <summary>
        /// Executes a stored procedure with optional parameters.
        /// </summary>
        private DataTable ExecuteProcedure(DatabaseContext databaseContext, string procedureName, string procedureArgs)
        {
            Logger.Info($"Executing stored procedure: {procedureName}");

            string query = $"EXEC {procedureName} {procedureArgs};";

            try
            {
                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                Logger.Success($"Stored procedure '{procedureName}' executed.");
                Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(result));

                return result;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error executing stored procedure '{procedureName}': {ex.Message}");
                return new DataTable();  // Return an empty table to prevent crashes
            }
        }

        /// <summary>
        /// Reads the definition of a stored procedure.
        /// </summary>
        private object? ReadProcedureDefinition(DatabaseContext databaseContext, string procedureName)
        {
            Logger.NewLine();
            Logger.Info($"Retrieving definition of stored procedure: {procedureName}");

            string query = $@"
                SELECT 
                    m.definition
                FROM sys.sql_modules AS m
                INNER JOIN sys.objects AS o ON m.object_id = o.object_id
                WHERE o.type = 'P' AND o.name = '{procedureName}';";

            try
            {
               DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result.Rows.Count == 0)
                {
                    Logger.Warning($"Stored procedure '{procedureName}' not found.");
                    return null;
                }

                string definition = result.Rows[0]["definition"].ToString();
                Logger.Success($"Stored procedure '{procedureName}' definition retrieved.");
                Console.WriteLine($"\n```sql\n{definition}\n```\n");

                return definition;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error retrieving stored procedure definition: {ex.Message}");
                return null;
            }
        }
    }
}