using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Collections.Generic;
using System.Data;

namespace MSSQLand.Actions.Domain
{
    /// <summary>
    /// RID enumeration via cycling through RIDs using SUSER_SNAME(SID_BINARY('S-...-RID')).
    /// </summary>
    internal class RidCycle : BaseAction
    {
        private const int DefaultMaxRid = 10000;
        private const int BatchSize = 1000;

        [ArgumentMetadata(Position = 0, Description = "Maximum RID to enumerate (default: 10000)")]
        private int _maxRid = DefaultMaxRid;

        [ExcludeFromArguments]
        private bool _bashOutput = false;
        
        [ExcludeFromArguments]
        private bool _pythonOutput = false;
        
        [ExcludeFromArguments]
        private bool _tableOutput = false;

        public override void ValidateArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return;
            }

            var (namedArgs, positionalArgs) = ParseActionArguments(args);

            // Check for --format flag
            if (namedArgs.ContainsKey("format"))
            {
                string format = namedArgs["format"].ToLower();
                if (format == "bash")
                {
                    _bashOutput = true;
                }
                else if (format == "python" || format == "py")
                {
                    _pythonOutput = true;
                }
                else if (format == "table")
                {
                    _tableOutput = true;
                }
                else
                {
                    throw new ArgumentException($"Invalid format: {format}. Use 'bash', 'python', or 'table'.");
                }
            }

            // First positional argument is max RID
            if (positionalArgs.Count > 0)
            {
                if (int.TryParse(positionalArgs[0], out int maxRid) && maxRid > 0)
                {
                    _maxRid = maxRid;
                }
                else
                {
                    throw new ArgumentException($"Invalid max RID: {positionalArgs[0]}. Must be a positive integer.");
                }
            }

            if (positionalArgs.Count > 1)
            {
                throw new ArgumentException($"Too many positional arguments. Expected: [maxRid]. Use --format flag for output format.");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Starting RID cycling (max RID: {_maxRid})");
            
            var results = new List<Dictionary<string, object>>();

            try
            {
                // Use AdDomain action to get domain SID information
                var AdDomainAction = new AdDomain();
                AdDomainAction.ValidateArguments(new string[0]);
                
                var domainInfo = AdDomainAction.Execute(databaseContext) as Dictionary<string, string>;
                
                if (domainInfo == null)
                {
                    Logger.Error("Failed to retrieve domain SID. Cannot proceed with RID cycling.");
                    return results;
                }

                string domain = domainInfo["Domain"];
                string AdDomainPrefix = domainInfo["Domain SID"];
                
                Logger.TaskNested($"Target domain: {domain}");
                Logger.TaskNested($"Domain SID prefix: {AdDomainPrefix}");

                // Iterate in batches
                int foundCount = 0;
                for (int start = 0; start <= _maxRid; start += BatchSize)
                {
                    int sidsToCheck = Math.Min(BatchSize, _maxRid - start + 1);
                    if (sidsToCheck == 0) break;
                    
                    // Build semicolon-separated SELECT statements
                    var queries = new List<string>();
                    for (int i = 0; i < sidsToCheck; i++)
                    {
                        int rid = start + i;
                        queries.Add($"SELECT SUSER_SNAME(SID_BINARY(N'{AdDomainPrefix}-{rid}'))");
                    }
                    
                    string sql = string.Join("; ", queries);
                    
                    try
                    {
                        // Execute returns SqlDataReader which can handle multiple result sets
                        using var reader = databaseContext.QueryService.Execute(sql);
                        int resultIndex = 0;

                        // Loop through all result sets
                        do
                        {
                            // Read the single row from this result set
                            if (reader.Read())
                            {
                                object oName = reader.GetValue(0);

                                if (oName != null && oName != DBNull.Value)
                                {
                                    string username = oName.ToString();

                                    // Skip NULL or empty results
                                    if (!string.IsNullOrEmpty(username) && !username.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                                    {
                                        int foundRid = start + resultIndex;
                                        string accountName = username.Contains("\\")
                                            ? username.Substring(username.IndexOf('\\') + 1)
                                            : username;

                                        Logger.Success($"RID {foundRid}: {username}");
                                        foundCount++;

                                        results.Add(new Dictionary<string, object>
                                        {
                                            ["RID"] = foundRid,
                                            ["Domain"] = domain,
                                            ["Username"] = accountName,
                                            ["Full Account"] = username
                                        });
                                    }
                                }
                            }

                            resultIndex++;
                        } while (reader.NextResult()); // Move to next result set
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Batch failed for RIDs {start}-{start + sidsToCheck - 1}: {ex.Message}");
                        continue;
                    }
                }

                Logger.Success($"RID cycling completed. Found {foundCount} domain accounts.");

                // Print results if any found
                if (results.Count > 0)
                {
                    if (_bashOutput)
                    {
                        // Output in bash associative array format
                        Logger.Info("Bash associative array format");
                        Console.WriteLine();
                        Console.WriteLine("declare -A rid_users=(");
                        
                        foreach (var entry in results)
                        {
                            string rid = entry["RID"].ToString();
                            string username = entry["Username"].ToString();
                            // Escape single quotes in username if present
                            username = username.Replace("'", "'\\''");
                            Console.WriteLine($"  [{rid}]='{username}'");
                        }
                        
                        Console.WriteLine(")");
                    }
                    else if (_pythonOutput)
                    {
                        // Output in Python dictionary format
                        Logger.Info("Python dictionary format");
                        Console.WriteLine();
                        Console.WriteLine("rid_users = {");
                        
                        int count = 0;
                        foreach (var entry in results)
                        {
                            string rid = entry["RID"].ToString();
                            string username = entry["Username"].ToString();
                            // Escape backslashes and single quotes for Python strings
                            username = username.Replace("\\", "\\\\").Replace("'", "\\'");
                            
                            string comma = (++count < results.Count) ? "," : "";
                            Console.WriteLine($"    {rid}: '{username}'{comma}");
                        }
                        
                        Console.WriteLine("}");
                    }
                    else if (_tableOutput)
                    {
                        // Detailed table output
                        DataTable resultTable = new();
                        resultTable.Columns.Add("RID", typeof(int));
                        resultTable.Columns.Add("Domain", typeof(string));
                        resultTable.Columns.Add("Username", typeof(string));
                        resultTable.Columns.Add("Full Account", typeof(string));

                        foreach (var entry in results)
                        {
                            resultTable.Rows.Add(
                                entry["RID"],
                                entry["Domain"],
                                entry["Username"],
                                entry["Full Account"]
                            );
                        }

                        Console.WriteLine(OutputFormatter.ConvertDataTable(resultTable));
                    }
                    else
                    {
                        // Default: simple line-by-line username output (pipe-friendly)
                        foreach (var entry in results)
                        {
                            Console.WriteLine(entry["Username"].ToString());
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error($"RID enumeration failed: {e.Message}");
                Logger.TraceNested($"Stack trace: {e.StackTrace}");
            }

            return results;
        }
    }
}
