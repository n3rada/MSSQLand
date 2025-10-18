using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Data;
using System.Security.Principal;

namespace MSSQLand.Actions.Domain
{
    /// <summary>
    /// RID enumeration via SUSER_SNAME(SID_BINARY('S-...-RID')) using DMV / SERVERPROPERTY-derived domain SID.
    /// </summary>
    internal class RidBrute : BaseAction
    {
        private const int DefaultMaxRid = 10000;      // change if you want larger by default
        private const int BatchSize = 1000;           // number of RIDs checked per batch

        private int _maxRid = DefaultMaxRid;

        public override void ValidateArguments(string additionalArguments)
        {
            // Optional: parse max RID from additional arguments
            if (!string.IsNullOrWhiteSpace(additionalArguments))
            {
                if (int.TryParse(additionalArguments, out int maxRid) && maxRid > 0)
                {
                    _maxRid = maxRid;
                }
                else
                {
                    throw new ArgumentException($"Invalid max RID value: {additionalArguments}. Must be a positive integer.");
                }
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Starting RID bruteforce (max RID: {_maxRid})");
            
            var results = new List<Dictionary<string, object>>();

            try
            {
                // Use DomainSid action to get domain SID information
                var domainSidAction = new DomainSid();
                domainSidAction.ValidateArguments(null);
                
                var domainInfo = domainSidAction.Execute(databaseContext) as Dictionary<string, string>;
                
                if (domainInfo == null)
                {
                    Logger.Error("Failed to retrieve domain SID. Cannot proceed with RID bruteforce.");
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
                Logger.Success($"RID bruteforce completed. Found {foundCount} domain accounts.");

                // Print results as table if any found
                if (results.Count > 0)
                {
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
