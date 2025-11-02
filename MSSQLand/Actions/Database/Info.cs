using MSSQLand.Services;
using MSSQLand.Utilities;
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
            "Server Name": "SELECT @@SERVERNAME;",
            "Default Domain": "SELECT DEFAULT_DOMAIN();",
            "Host Name": "SELECT CAST(SERVERPROPERTY('MachineName') AS NVARCHAR(256));",
            "Operating System Version": "SELECT TOP(1) windows_release + ISNULL(' ' + windows_service_pack_level, '') FROM sys.dm_os_windows_info;",
            "SQL Service Process ID": "SELECT CAST(SERVERPROPERTY('ProcessId') AS INT);",
            "SQL Service Account": "SELECT CAST(SERVERPROPERTY('ServiceAccountName') AS NVARCHAR(256));",
            "Instance Name": "SELECT ISNULL(CAST(SERVERPROPERTY('InstanceName') AS NVARCHAR(256)), 'DEFAULT');",
            "Authentication Mode": "SELECT CASE CAST(SERVERPROPERTY('IsIntegratedSecurityOnly') AS INT) WHEN 1 THEN 'Windows Authentication only' ELSE 'Mixed mode (Windows + SQL)' END;",
            "Clustered Server": "SELECT CASE CAST(SERVERPROPERTY('IsClustered') AS INT) WHEN 0 THEN 'No' ELSE 'Yes' END;",
            "SQL Version": "SELECT CAST(SERVERPROPERTY('ProductVersion') AS NVARCHAR(256));",
            "SQL Major Version": "SELECT SUBSTRING(@@VERSION, CHARINDEX('2', @@VERSION), 4);",
            "SQL Edition": "SELECT CAST(SERVERPROPERTY('Edition') AS NVARCHAR(256));",
            "SQL Service Pack": "SELECT CAST(SERVERPROPERTY('ProductLevel') AS NVARCHAR(256));",
            "OS Architecture": "SELECT SUBSTRING(@@VERSION, CHARINDEX('x', @@VERSION), 3);",
            "OS Version Number": "SELECT RIGHT(SUBSTRING(@@VERSION, CHARINDEX('Windows Server', @@VERSION), 19), 4);",
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


            Console.WriteLine(MarkdownFormatter.ConvertDictionaryToMarkdownTable(results, "Information", "Value"));

            return results;

        }
    }
}
