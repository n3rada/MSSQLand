using MSSQLand.Services;
using MSSQLand.Utilities;
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

        public override void ValidateArguments(string additionalArguments)
        {
            if (string.IsNullOrWhiteSpace(additionalArguments))
            {
                return;
            }

            string[] parts = SplitArguments(additionalArguments);

            foreach (var part in parts)
            {
                string arg = part.Trim();
                
                if (arg.Equals("bash", StringComparison.OrdinalIgnoreCase))
                {
                    _bashOutput = true;
                }
                else if (arg.Equals("python", StringComparison.OrdinalIgnoreCase) || arg.Equals("py", StringComparison.OrdinalIgnoreCase))
                {
                    _pythonOutput = true;
                }
                else if (int.TryParse(arg, out int maxRid) && maxRid > 0)
                {
                    _maxRid = maxRid;
                }
                else
                {
                    throw new ArgumentException($"Invalid argument: {arg}. Use a positive integer for max RID, 'bash' for bash output, or 'python'/'py' for Python output.");
                }
            }

            // Both cannot be enabled at the same time
            if (_bashOutput && _pythonOutput)
            {
                throw new ArgumentException("Cannot use both 'bash' and 'python' output formats simultaneously. Choose one.");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Starting RID cycling (max RID: {_maxRid})");
            Logger.Info("Note: This enumerates domain objects (users and groups), not group membership.");
            Logger.Info("Use 'groupmembers DOMAIN\\GroupName' to see members of a specific group.");
            Logger.NewLine();
            
            var results = new List<Dictionary<string, object>>();

            try
            {
                // Use DomainSid action to get domain SID information
                var domainSidAction = new DomainSid();
                domainSidAction.ValidateArguments(null);
                
                var domainInfo = domainSidAction.Execute(databaseContext) as Dictionary<string, string>;
                
                if (domainInfo == null)
                {
                    Logger.Error("Failed to retrieve domain SID. Cannot proceed with RID cycling.");
                    return results;
                }

                string domain = domainInfo["Domain"];
                string domainSidPrefix = domainInfo["Domain SID"];
                
                Logger.Info($"Target domain: {domain}");
                Logger.Info($"Domain SID prefix: {domainSidPrefix}");
                Logger.NewLine();

                // Iterate in batches - use semicolon-separated queries like Python
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
                        queries.Add($"SELECT SUSER_SNAME(SID_BINARY(N'{domainSidPrefix}-{rid}'))");
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

                Logger.NewLine();
                Logger.Success($"RID cycling completed. Found {foundCount} domain accounts.");

                // Print results as table if any found
                if (results.Count > 0)
                {
                    if (_bashOutput)
                    {
                        // Output in bash associative array format
                        Logger.Info("Bash associative array format:");
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
                        Console.WriteLine();
                        Console.WriteLine("# Usage example:");
                        Console.WriteLine("# for rid in \"${!rid_users[@]}\"; do");
                        Console.WriteLine("#   echo \"RID: $rid - User: ${rid_users[$rid]}\"");
                        Console.WriteLine("# done");
                    }
                    else if (_pythonOutput)
                    {
                        // Output in Python dictionary format
                        Logger.Info("Python dictionary format:");
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
                        Console.WriteLine();
                        Console.WriteLine("# Usage example:");
                        Console.WriteLine("# for rid, username in rid_users.items():");
                        Console.WriteLine("#     print(f\"RID: {rid} - User: {username}\")");
                        Console.WriteLine("#");
                        Console.WriteLine("# # Direct lookup:");
                        Console.WriteLine("# print(f\"User with RID 1001: {rid_users.get(1001, 'Not found')}\")");
                    }
                    else
                    {
                        // Standard markdown table output
                        DataTable resultTable = new DataTable();
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

                        Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(resultTable));
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error($"RID enumeration failed: {e.Message}");
                if (Logger.IsDebugEnabled)
                {
                    Logger.DebugNested($"Stack trace: {e.StackTrace}");
                }
            }

            return results;
        }
    }
}
