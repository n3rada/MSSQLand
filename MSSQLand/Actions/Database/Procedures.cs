using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;

namespace MSSQLand.Actions.Database
{
    internal class Procedures : BaseAction
    {
        private enum Mode { List, Exec, Read, Search, Sqli }
        
        [ArgumentMetadata(Position = 0, Description = "Mode: list, exec, read, search, or sqli (default: list)")]
        private Mode _mode = Mode.List;
        
        [ArgumentMetadata(Position = 1, Description = "Stored procedure name (required for exec/read) or search keyword (required for search)")]
        private string? _procedureName;
        
        [ArgumentMetadata(Position = 2, Description = "Procedure arguments (optional for exec)")]
        private string? _procedureArgs;
        
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
                case Mode.Search:
                    return SearchProcedures(databaseContext, _searchKeyword);
                case Mode.Sqli:
                    return FindSqliVulnerable(databaseContext);
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
            Logger.Task("Retrieving all stored procedures in the database");

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

            Logger.NewLine();
            Logger.Info($"Total: {procedures.Rows.Count} stored procedure(s) found");

            return procedures;
        }

        /// <summary>
        /// Executes a stored procedure with optional parameters.
        /// </summary>
        private DataTable ExecuteProcedure(DatabaseContext databaseContext, string procedureName, string procedureArgs)
        {
            Logger.Task($"Executing stored procedure: {procedureName}");

            string query = $"EXEC {procedureName} {procedureArgs};";

            try
            {
                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                Logger.Success($"Stored procedure '{procedureName}' executed.");
                Logger.NewLine();
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
            Logger.Task($"Retrieving definition of stored procedure: {procedureName}");

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

        /// <summary>
        /// Searches for stored procedures containing a specific keyword in their definition.
        /// </summary>
        private DataTable SearchProcedures(DatabaseContext databaseContext, string keyword)
        {
            Logger.NewLine();
            Logger.Info($"Searching for stored procedures containing keyword: '{keyword}'");

            string query = $@"
                SELECT 
                    SCHEMA_NAME(o.schema_id) AS schema_name,
                    o.name AS procedure_name,
                    o.create_date,
                    o.modify_date
                FROM sys.sql_modules AS m
                INNER JOIN sys.objects AS o ON m.object_id = o.object_id
                WHERE o.type = 'P' 
                AND m.definition LIKE '%{keyword}%'
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
                Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(result));

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

        /// <summary>
        /// Finds stored procedures potentially vulnerable to SQL injection by detecting dynamic SQL execution with parameters.
        /// </summary>
        private DataTable FindSqliVulnerable(DatabaseContext databaseContext)
        {
            Logger.NewLine();
            Logger.Info("Searching for stored procedures potentially vulnerable to SQL injection...");

            string query = @"
                SELECT 
                    SCHEMA_NAME(o.schema_id) AS schema_name,
                    o.name AS procedure_name,
                    o.create_date,
                    o.modify_date,
                    m.definition
                FROM sys.sql_modules AS m
                INNER JOIN sys.objects AS o ON m.object_id = o.object_id
                WHERE o.type = 'P' 
                AND (
                    -- EXEC with string concatenation using parameters
                    (m.definition LIKE '%EXEC (%' AND m.definition LIKE '%+%@%')
                    OR (m.definition LIKE '%EXECUTE (%' AND m.definition LIKE '%+%@%')
                    -- Direct variable execution
                    OR m.definition LIKE '%EXEC(@%'
                    OR m.definition LIKE '%EXECUTE(@%'
                    -- sp_executesql with concatenation
                    OR (m.definition LIKE '%sp_executesql%' AND m.definition LIKE '%+%@%')
                )
                ORDER BY o.modify_date DESC;";

            try
            {
                DataTable fullResult = databaseContext.QueryService.ExecuteTable(query);

                if (fullResult.Rows.Count == 0)
                {
                    Logger.Success("No potentially vulnerable stored procedures found.");
                    return new DataTable();
                }

                // Create result table with vulnerability details
                DataTable result = new();
                result.Columns.Add("schema_name", typeof(string));
                result.Columns.Add("procedure_name", typeof(string));
                result.Columns.Add("modify_date", typeof(DateTime));

                foreach (DataRow row in fullResult.Rows)
                {
                    string definition = row["definition"].ToString();
                    string schemaName = row["schema_name"].ToString();
                    string procName = row["procedure_name"].ToString();
                    DateTime modifyDate = (DateTime)row["modify_date"];

                    bool isVulnerable = false;

                    // Check if EXEC/EXECUTE with concatenation is NOT inside quotes
                    // Pattern: EXEC ('string' + @param) outside of quoted strings
                    if (ContainsDynamicExecOutsideQuotes(definition))
                    {
                        isVulnerable = true;
                    }

                    if (isVulnerable)
                    {
                        result.Rows.Add(schemaName, procName, modifyDate);
                    }
                }

                if (result.Rows.Count == 0)
                {
                    Logger.Success("No high-risk SQL injection patterns found.");
                    return result;
                }

                Logger.Warning($"Found {result.Rows.Count} stored procedure(s) with potential SQL injection vulnerabilities!");
                Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(result));

                Logger.NewLine();
                Logger.Info($"Total: {result.Rows.Count} potentially vulnerable stored procedure(s) found");
                Logger.Info("Review these procedures manually. Use '/a:procedures read <name>' to inspect the full definition.");

                return result;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error searching for SQL injection vulnerabilities: {ex.Message}");
                return new DataTable();
            }
        }

        /// <summary>
        /// Checks if a procedure contains dynamic EXEC/EXECUTE with string concatenation outside of quoted strings.
        /// </summary>
        private bool ContainsDynamicExecOutsideQuotes(string definition)
        {
            // Remove all string literals to simplify pattern matching
            string cleanedDefinition = RemoveStringLiterals(definition);
            
            // Pattern 1: EXEC( ... + @param ...) or EXECUTE( ... + @param ...)
            if (System.Text.RegularExpressions.Regex.IsMatch(cleanedDefinition, 
                @"EXEC(UTE)?\s*\([^)]*\+\s*@", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return true;
            }
            
            // Pattern 2: EXEC( @param + ...) or EXECUTE( @param + ...)
            if (System.Text.RegularExpressions.Regex.IsMatch(cleanedDefinition, 
                @"EXEC(UTE)?\s*\(\s*@[^\s)]*\s*\+", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return true;
            }
            
            // Pattern 3: EXEC @variable or EXECUTE @variable (direct variable execution without parentheses)
            if (System.Text.RegularExpressions.Regex.IsMatch(cleanedDefinition, 
                @"EXEC(UTE)?\s+@[a-zA-Z_][\w]*\s*($|;|\s)", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return true;
            }
            
            // Pattern 4: EXEC(@variable) or EXECUTE(@variable) (direct variable execution with parentheses)
            if (System.Text.RegularExpressions.Regex.IsMatch(cleanedDefinition, 
                @"EXEC(UTE)?\s*\(\s*@[a-zA-Z_][\w]*\s*\)", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return true;
            }
            
            // Pattern 5: sp_executesql with concatenation
            if (System.Text.RegularExpressions.Regex.IsMatch(cleanedDefinition, 
                @"sp_executesql[^;]*\+\s*@", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Removes string literals from SQL code to simplify pattern matching.
        /// Handles SQL's '' escape sequence correctly.
        /// </summary>
        private string RemoveStringLiterals(string sql)
        {
            var result = new System.Text.StringBuilder();
            int i = 0;
            
            while (i < sql.Length)
            {
                // Check for single-quoted string
                if (sql[i] == '\'')
                {
                    i++; // Skip opening quote
                    
                    // Skip until closing quote, handling '' escape sequence
                    while (i < sql.Length)
                    {
                        if (sql[i] == '\'')
                        {
                            // Check if it's an escaped quote ''
                            if (i + 1 < sql.Length && sql[i + 1] == '\'')
                            {
                                i += 2; // Skip both quotes
                            }
                            else
                            {
                                i++; // Skip closing quote
                                break;
                            }
                        }
                        else
                        {
                            i++;
                        }
                    }
                }
                // Check for double-quoted identifier (SQL Server supports this with QUOTED_IDENTIFIER ON)
                else if (sql[i] == '"')
                {
                    result.Append(sql[i]); // Keep double quotes as they might be identifiers
                    i++;
                }
                // Check for single-line comment --
                else if (i + 1 < sql.Length && sql[i] == '-' && sql[i + 1] == '-')
                {
                    // Skip until end of line
                    while (i < sql.Length && sql[i] != '\n' && sql[i] != '\r')
                    {
                        i++;
                    }
                }
                // Check for multi-line comment /* */
                else if (i + 1 < sql.Length && sql[i] == '/' && sql[i + 1] == '*')
                {
                    i += 2;
                    // Skip until */
                    while (i + 1 < sql.Length)
                    {
                        if (sql[i] == '*' && sql[i + 1] == '/')
                        {
                            i += 2;
                            break;
                        }
                        i++;
                    }
                }
                else
                {
                    result.Append(sql[i]);
                    i++;
                }
            }
            
            return result.ToString();
        }
    }
}