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
            { "Server Name", "SELECT @@SERVERNAME;" },
            { "Default Domain", "SELECT DEFAULT_DOMAIN();" },
            { "Host Name", "SELECT SERVERPROPERTY('MachineName');" },
            { "Operating System Version", "SELECT TOP(1) windows_release + ISNULL(' ' + windows_service_pack_level, '') FROM master.sys.dm_os_windows_info;" },
            { "SQL Service Process ID", "SELECT SERVERPROPERTY('ProcessId');" },
            { "Instance Name", "SELECT ISNULL(SERVERPROPERTY('InstanceName'), 'DEFAULT');" },
            { "Authentication Mode", "SELECT CASE SERVERPROPERTY('IsIntegratedSecurityOnly') WHEN 1 THEN 'Windows Authentication only' ELSE 'Mixed mode (Windows + SQL)' END;" },
            { "Clustered Server", "SELECT CASE SERVERPROPERTY('IsClustered') WHEN 0 THEN 'No' ELSE 'Yes' END;" },
            { "SQL Version", "SELECT SERVERPROPERTY('ProductVersion');" },
            { "SQL Major Version", "SELECT SUBSTRING(@@VERSION, CHARINDEX('2', @@VERSION), 4);" },
            { "SQL Edition", "SELECT SERVERPROPERTY('Edition');" },
            { "SQL Service Pack", "SELECT SERVERPROPERTY('ProductLevel');" },
            { "OS Architecture", "SELECT SUBSTRING(@@VERSION, CHARINDEX('x', @@VERSION), 3);" },
            { "OS Version Number", "SELECT RIGHT(SUBSTRING(@@VERSION, CHARINDEX('Windows Server', @@VERSION), 19), 4);" },
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

                    results[key] = result;
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
