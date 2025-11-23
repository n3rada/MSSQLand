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
        private readonly Dictionary<string, Dictionary<string, string>> _queries = new()
        {
            {
                "all", new Dictionary<string, string>
                {
                    // Server Identification
                    { "Server Name", "SELECT @@SERVERNAME;" },
                    { "Instance Name", "SELECT ISNULL(SERVERPROPERTY('InstanceName'), 'DEFAULT');" },
                    { "Computer Name", "SELECT SERVERPROPERTY('ComputerNamePhysicalNetBIOS');" },
                    { "Server Collation", "SELECT SERVERPROPERTY('Collation');" },
                    { "Default Domain", "SELECT DEFAULT_DOMAIN();" },
                    { "Current Database", "SELECT DB_NAME();" },
                    { "Current Language", "SELECT @@LANGUAGE;" },
                    { "Server Language", "SELECT SERVERPROPERTY('ServerCollation');" },
                    
                    // SQL Server Information
                    { "SQL Version", "SELECT SERVERPROPERTY('ProductVersion');" },
                    { "SQL Edition", "SELECT SERVERPROPERTY('Edition');" },
                    { "SQL Service Pack", "SELECT SERVERPROPERTY('ProductLevel');" },
                    { "SQL Update Level", "SELECT SERVERPROPERTY('ProductUpdateLevel');" },
                    { "Engine Edition", "SELECT CASE CAST(SERVERPROPERTY('EngineEdition') AS INT) WHEN 1 THEN 'Personal/Desktop' WHEN 2 THEN 'Standard' WHEN 3 THEN 'Enterprise' WHEN 4 THEN 'Express' WHEN 5 THEN 'Azure SQL Database' WHEN 6 THEN 'Azure Synapse' WHEN 8 THEN 'Azure SQL MI' WHEN 9 THEN 'Azure SQL Edge' WHEN 11 THEN 'Azure Synapse Serverless' ELSE 'Unknown' END;" },
                    
                    // Licensing & Features
                    { "License Type", "SELECT SERVERPROPERTY('LicenseType');" },
                    { "Number of Licenses", "SELECT SERVERPROPERTY('NumLicenses');" },
                    { "FileStream Configured", "SELECT CASE SERVERPROPERTY('FilestreamConfiguredLevel') WHEN 0 THEN 'Disabled' WHEN 1 THEN 'T-SQL access only' WHEN 2 THEN 'T-SQL and file I/O access' WHEN 3 THEN 'Remote client access' ELSE 'Unknown' END;" },
                    { "FileStream Effective Level", "SELECT CASE SERVERPROPERTY('FilestreamEffectiveLevel') WHEN 0 THEN 'Disabled' WHEN 1 THEN 'T-SQL access only' WHEN 2 THEN 'T-SQL and file I/O access' WHEN 3 THEN 'Remote client access' ELSE 'Unknown' END;" },
                    { "FileStream Share Name", "SELECT SERVERPROPERTY('FilestreamShareName');" },
                    { "Full-Text Installed", "SELECT CASE SERVERPROPERTY('IsFullTextInstalled') WHEN 1 THEN 'Yes' ELSE 'No' END;" },
                    { "Is Single User", "SELECT CASE SERVERPROPERTY('IsSingleUser') WHEN 1 THEN 'Yes' ELSE 'No' END;" },
                    { "Is Local DB", "SELECT CASE SERVERPROPERTY('IsLocalDB') WHEN 1 THEN 'Yes' ELSE 'No' END;" },
                    { "Is PolyBase Installed", "SELECT CASE SERVERPROPERTY('IsPolyBaseInstalled') WHEN 1 THEN 'Yes' ELSE 'No' END;" },
                    
                    // Configuration
                    { "Authentication Mode", "SELECT CASE SERVERPROPERTY('IsIntegratedSecurityOnly') WHEN 1 THEN 'Windows Authentication only' ELSE 'Mixed mode (Windows + SQL)' END;" },
                    { "Clustered Server", "SELECT CASE SERVERPROPERTY('IsClustered') WHEN 0 THEN 'No' ELSE 'Yes' END;" },
                    { "Resource Version", "SELECT SERVERPROPERTY('ResourceVersion');" },
                    { "Resource Last Update", "SELECT SERVERPROPERTY('ResourceLastUpdateDateTime');" },
                    { "Server Started", "SELECT sqlserver_start_time FROM sys.dm_os_sys_info;" },
                    { "Server Uptime (Days)", "SELECT DATEDIFF(DAY, sqlserver_start_time, GETDATE()) FROM sys.dm_os_sys_info;" },
                    { "CPU Count", "SELECT cpu_count FROM sys.dm_os_sys_info;" },
                    { "Physical Memory (MB)", "SELECT physical_memory_kb / 1024 FROM sys.dm_os_sys_info;" },
                    { "Virtual Memory (MB)", "SELECT virtual_memory_kb / 1024 FROM sys.dm_os_sys_info;" },
                    
                    // Full Version
                    { "Full Version String", "SELECT @@VERSION;" }
                }
            },
            {
                "on-premises", new Dictionary<string, string>
                {
                    { "Host Name", "SELECT SERVERPROPERTY('MachineName');" },
                    { "SQL Service Process ID", "SELECT SERVERPROPERTY('ProcessId');" },
                    { "SQL Service Account", "SELECT service_account FROM sys.dm_server_services WHERE servicename LIKE 'SQL Server (%';" },
                    { "SQL Service Start Mode", "SELECT startup_type_desc FROM sys.dm_server_services WHERE servicename LIKE 'SQL Server (%';" },
                    { "SQL Agent Service Account", "SELECT service_account FROM sys.dm_server_services WHERE servicename LIKE 'SQL Server Agent%';" },
                    { "SQL Agent Status", "SELECT status_desc FROM sys.dm_server_services WHERE servicename LIKE 'SQL Server Agent%';" },
                    { "Operating System Version", "SELECT TOP(1) windows_release + ISNULL(' ' + windows_service_pack_level, '') FROM master.sys.dm_os_windows_info;" },
                    { "OS Language Version", "SELECT TOP(1) os_language_version FROM master.sys.dm_os_windows_info;" },
                    { "OS Architecture", "SELECT CASE WHEN CAST(SERVERPROPERTY('Edition') AS NVARCHAR(128)) LIKE '%64%' THEN '64-bit' ELSE '32-bit' END;" },
                }
            },
            {
                "azure", new Dictionary<string, string>
                {
                    { "Azure Service Tier", "SELECT DATABASEPROPERTYEX(DB_NAME(), 'ServiceObjective');" },
                    { "Azure Database Edition", "SELECT DATABASEPROPERTYEX(DB_NAME(), 'Edition');" },
                    { "Azure Max Database Size", "SELECT CAST(DATABASEPROPERTYEX(DB_NAME(), 'MaxSizeInBytes') AS BIGINT);" },
                    { "Azure Engine Edition", "SELECT SERVERPROPERTY('EngineEdition');" }
                }
            }
        };

        public override void ValidateArguments(string[] args)
        {
            // No additional arguments needed
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Retrieving SQL Server information");
            
            var results = new Dictionary<string, string>();
            bool isAzureSQL = databaseContext.QueryService.IsAzureSQL();

            // Determine which query sets to use
            var querySets = new List<Dictionary<string, string>> { _queries["all"] };
            
            if (isAzureSQL)
            {
                querySets.Add(_queries["azure"]);
            }
            else
            {
                querySets.Add(_queries["on-premises"]);
            }

            // Execute all queries from the selected sets
            foreach (var querySet in querySets)
            {
                foreach (var entry in querySet)
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

                        // Special handling for Azure Max Database Size
                        if (key == "Azure Max Database Size")
                        {
                            if (long.TryParse(result, out long bytes) && bytes > 0)
                            {
                                result = $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
                            }
                            else
                            {
                                result = "Unlimited or default";
                            }
                        }

                        // Special handling for Azure Engine Edition
                        if (key == "Azure Engine Edition")
                        {
                            string description = result switch
                            {
                                "1" => "Personal or Desktop Engine",
                                "2" => "Standard",
                                "3" => "Enterprise",
                                "4" => "Express",
                                "5" => "Azure SQL Database",
                                "6" => "Azure Synapse Analytics",
                                "8" => "Azure SQL Managed Instance",
                                "9" => "Azure SQL Edge",
                                "11" => "Azure Synapse serverless SQL pool",
                                _ => "Unknown"
                            };
                            result = $"{result} ({description})";
                        }

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
            }

            Console.WriteLine(OutputFormatter.ConvertDictionary(results, "Information", "Value"));

            return results;

        }
    }
}
