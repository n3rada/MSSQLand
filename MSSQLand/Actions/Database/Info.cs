using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Collections.Generic;
using System.Data;


namespace MSSQLand.Actions.Database
{
    /// <summary>
    /// Retrieving information of current DBMS server using DMVs and SERVERPROPERTY only.
    /// No registry access.
    /// </summary>
    internal class Info : BaseAction
    {
        [ExcludeFromArguments]
        private readonly Dictionary<string, string> _queries = new()
        {
            // Server Identification
            { "Server Name", "SELECT @@SERVERNAME;" },
            { "Host Name", "SELECT SERVERPROPERTY('MachineName');" },
            { "Instance Name", "SELECT ISNULL(SERVERPROPERTY('InstanceName'), 'DEFAULT');" },
            { "Default Domain", "SELECT DEFAULT_DOMAIN();" },
            
            // SQL Server Information
            { "SQL Version", "SELECT SERVERPROPERTY('ProductVersion');" },
            { "SQL Major Version", "SELECT SERVERPROPERTY('ProductMajorVersion');" },
            { "SQL Edition", "SELECT SERVERPROPERTY('Edition');" },
            { "SQL Service Pack", "SELECT SERVERPROPERTY('ProductLevel');" },
            { "SQL Service Process ID", "SELECT SERVERPROPERTY('ProcessId');" },
            
            // Configuration
            { "Authentication Mode", "SELECT CASE SERVERPROPERTY('IsIntegratedSecurityOnly') WHEN 1 THEN 'Windows Authentication only' ELSE 'Mixed mode (Windows + SQL)' END;" },
            { "Clustered Server", "SELECT CASE SERVERPROPERTY('IsClustered') WHEN 0 THEN 'No' ELSE 'Yes' END;" },
            
            // Operating System
            { "Operating System Version", "SELECT TOP(1) windows_release + ISNULL(' ' + windows_service_pack_level, '') FROM master.sys.dm_os_windows_info;" },
            { "OS Architecture", "SELECT CASE WHEN CAST(SERVERPROPERTY('Edition') AS NVARCHAR(128)) LIKE '%64%' THEN '64-bit' ELSE '32-bit' END;" },
            { "Full Version String", "SELECT @@VERSION;" },
        };

        public override void ValidateArguments(string additionalArguments)
        {
            // No additional arguments needed
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            var results = new Dictionary<string, string>();

            foreach (var entry in _queries)
            {
                string key = entry.Key;
                string query = entry.Value;

                try
                {
                    DataTable queryResult = databaseContext.QueryService.ExecuteTable(query);

                    // Extract the first row and first column value if present
                    string result = queryResult.Rows.Count > 0
                        ? queryResult.Rows[0][0]?.ToString() ?? "NULL"
                        : "NULL";

                    // Split Full Version String into multiple rows with meaningful labels
                    if (key == "Full Version String")
                    {
                        var lines = result.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 0; i < lines.Length; i++)
                        {
                            string line = lines[i].Trim();
                            string lineKey;

                            // Determine the purpose of each line based on its content
                            if (line.StartsWith("Microsoft SQL", StringComparison.OrdinalIgnoreCase))
                            {
                                lineKey = "Product Version";
                            }
                            else if (line.StartsWith("Copyright", StringComparison.OrdinalIgnoreCase))
                            {
                                lineKey = "Copyright";
                            }
                            else if (line.Contains("Edition") && line.Contains("Licensing"))
                            {
                                lineKey = "Edition Details";
                            }
                            else if (line.Contains("Windows") && (line.Contains("Server") || line.Contains("Build")))
                            {
                                lineKey = "OS Details";
                            }
                            else if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^\w{3}\s+\d{1,2}\s+\d{4}"))
                            {
                                // Matches date patterns like "Oct 7 2025"
                                lineKey = "Build Date";
                            }
                            else
                            {
                                lineKey = $"Version Info (Line {i + 1})";
                            }

                            results[lineKey] = line;
                        }
                    }
                    else
                    {
                        results[key] = result;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to execute '{key}': {ex.Message}");
                    results[key] = $"ERROR: {ex.Message}";
                }
            }


            Console.WriteLine(OutputFormatter.ConvertDictionary(results, "Information", "Value"));

            return results;

        }
    }
}
