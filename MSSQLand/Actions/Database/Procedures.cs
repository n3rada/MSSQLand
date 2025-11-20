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
                    USER_NAME(OBJECTPROPERTY(object_id, 'OwnerId')) AS owner,
                    create_date,
                    modify_date
                FROM sys.procedures
                ORDER BY schema_name ASC, procedure_name ASC, modify_date DESC;";

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

            Console.WriteLine(OutputFormatter.ConvertDataTable(procedures));

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
                Console.WriteLine(OutputFormatter.ConvertDataTable(result));
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

        /// <summary>
        /// Finds stored procedures potentially vulnerable to SQL injection by detecting dynamic SQL execution with parameters.
        /// </summary>
        private DataTable FindSqliVulnerable(DatabaseContext databaseContext)
        {
            Logger.NewLine();
            Logger.Info("Searching for stored procedures potentially vulnerable to SQL injection");
            Logger.InfoNested("Results require manual verification, this is a heuristic detection");
            Logger.NewLine();

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
                    -- SET @var = @var + concatenation
                    OR (m.definition LIKE '%SET @%=%@%+%@%')
                    OR (m.definition LIKE '%SET @%=@%+%''%')
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
                result.Columns.Add("vulnerability_pattern", typeof(string));
                result.Columns.Add("vulnerable_line", typeof(string));
                result.Columns.Add("modify_date", typeof(DateTime));

                foreach (DataRow row in fullResult.Rows)
                {
                    string definition = row["definition"].ToString();
                    string schemaName = row["schema_name"].ToString();
                    string procName = row["procedure_name"].ToString();
                    DateTime modifyDate = (DateTime)row["modify_date"];

                    var vulnerabilities = FindVulnerablePatterns(definition);

                    foreach (var vuln in vulnerabilities)
                    {
                        result.Rows.Add(schemaName, procName, vuln.Pattern, vuln.Line, modifyDate);
                    }
                }

                if (result.Rows.Count == 0)
                {
                    Logger.Success("No high-risk SQL injection patterns found.");
                    return result;
                }

                Logger.Success($"Found {result.Rows.Count} potential SQL injection pattern(s) in stored procedures");
                Console.WriteLine(OutputFormatter.ConvertDataTable(result));

                Logger.NewLine();
                Logger.Warning("These results require manual verification.");
                Logger.WarningNested("Use '/a:procedures read <name>' to inspect the full definition.");

                return result;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error searching for SQL injection vulnerabilities: {ex.Message}");
                return new DataTable();
            }
        }

        /// <summary>
        /// Finds vulnerable patterns and returns the pattern type and the actual vulnerable line.
        /// </summary>
        private List<(string Pattern, string Line)> FindVulnerablePatterns(string definition)
        {
            var vulnerabilities = new List<(string, string)>();
            string[] lines = definition.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                
                // Skip comments and empty lines
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("--"))
                    continue;

                string lineUpper = line.ToUpper();
                
                // Pattern 1: Direct EXEC/EXECUTE with concatenation
                if (System.Text.RegularExpressions.Regex.IsMatch(line, 
                    @"EXEC(UTE)?\s*\([^)]*\+\s*@", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    vulnerabilities.Add(("EXEC with concatenation", TruncateLine(line, 80)));
                }
                
                // Pattern 2: Direct variable execution
                else if (System.Text.RegularExpressions.Regex.IsMatch(line, 
                    @"EXEC(UTE)?\s*\(\s*@[a-zA-Z_][\w]*\s*\)", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                    System.Text.RegularExpressions.Regex.IsMatch(line, 
                    @"EXEC(UTE)?\s+@[a-zA-Z_][\w]*\s*($|;|\s)", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    vulnerabilities.Add(("Direct variable execution", TruncateLine(line, 80)));
                }
                
                // Pattern 3: sp_executesql with concatenation (excluding safe parameterized patterns)
                else if (lineUpper.Contains("SP_EXECUTESQL") && line.Contains("+") && line.Contains("@"))
                {
                    // Check if it's NOT a safe parameterized query pattern
                    if (!System.Text.RegularExpressions.Regex.IsMatch(line, 
                        @"sp_executesql\s+@\w+\s*,\s*N'[^']*@", 
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        vulnerabilities.Add(("sp_executesql with concatenation", TruncateLine(line, 80)));
                    }
                }
                
                // Pattern 4: Building dynamic SQL with SET and concatenation
                else if (lineUpper.Contains("SET @") && line.Contains("=") && line.Contains("+"))
                {
                    // Check if it's concatenating with parameters or strings
                    if (System.Text.RegularExpressions.Regex.IsMatch(line, 
                        @"SET\s+@\w+\s*=\s*[^=]*\+\s*@", 
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        vulnerabilities.Add(("Dynamic SQL building", TruncateLine(line, 80)));
                    }
                }
                
                // Pattern 5: Multi-line concatenation (look for += pattern)
                else if (lineUpper.Contains("SET @") && (line.Contains("+=") || 
                    System.Text.RegularExpressions.Regex.IsMatch(line, 
                    @"SET\s+@(\w+)\s*=\s*@\1\s*\+", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
                {
                    vulnerabilities.Add(("Multi-line SQL concatenation", TruncateLine(line, 80)));
                }
            }

            return vulnerabilities;
        }

        /// <summary>
        /// Truncates a line to a maximum length and adds ellipsis if needed.
        /// </summary>
        private string TruncateLine(string line, int maxLength)
        {
            line = line.Trim();
            if (line.Length <= maxLength)
                return line;
            
            return line.Substring(0, maxLength - 3) + "...";
        }
    }
}