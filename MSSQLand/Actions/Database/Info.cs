using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Data;


namespace MSSQLand.Actions
{
    internal class Info : BaseAction
    {
        private readonly Dictionary<string, string> _queries = new()
        {
            { "Server Name", "SELECT @@SERVERNAME;" },
            { "Default Domain", "SELECT DEFAULT_DOMAIN();" },
            { "SQL Service Process ID", "SELECT SERVERPROPERTY('ProcessId');" },
            { "Machine Type", "EXEC xp_regread 'HKEY_LOCAL_MACHINE', 'SYSTEM\\CurrentControlSet\\Control\\ProductOptions', 'ProductType';" },
            { "Operating System Version", "EXEC xp_regread 'HKEY_LOCAL_MACHINE', 'SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion', 'ProductName';" },
            { "SQL Service Name", "SELECT SERVERPROPERTY('InstanceName') AS InstanceName;" },
            { "SQL Service Account Name", "EXEC xp_instance_regread N'HKEY_LOCAL_MACHINE', 'SYSTEM\\CurrentControlSet\\Services\\MSSQLSERVER', 'ObjectName';" },
            { "Authentication Mode", "SELECT CASE SERVERPROPERTY('IsIntegratedSecurityOnly') WHEN 1 THEN 'Windows Authentication' ELSE 'Mixed Authentication' END;" },
            { "Encryption Enforced", "EXEC xp_instance_regread N'HKEY_LOCAL_MACHINE', 'SOFTWARE\\Microsoft\\Microsoft SQL Server\\MSSQLServer\\SuperSocketNetLib', 'ForceEncryption';" },
            { "Clustered Server", "SELECT CASE SERVERPROPERTY('IsClustered') WHEN 0 THEN 'No' ELSE 'Yes' END;" },
            { "SQL Version", "SELECT SERVERPROPERTY('ProductVersion');" },
            { "SQL Major Version", "SELECT SUBSTRING(@@VERSION, CHARINDEX('2', @@VERSION), 4);" },
            { "SQL Edition", "SELECT SERVERPROPERTY('Edition');" },
            { "SQL Service Pack", "SELECT SERVERPROPERTY('ProductLevel');" },
            { "OS Architecture", "SELECT SUBSTRING(@@VERSION, CHARINDEX('x', @@VERSION), 3);" },
            { "OS VersionNumber", "SELECT RIGHT(SUBSTRING(@@VERSION, CHARINDEX('Windows Server', @@VERSION), 19), 4);" },
            { "Logged-in User", "SELECT SYSTEM_USER;" },
            { "Active SQL Sessions", "SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE status = 'running';" }
        };

        public override void ValidateArguments(string additionalArgument)
        {
            // No additional arguments needed
        }

        public override void Execute(DatabaseContext connectionManager)
        {
            var results = new Dictionary<string, string>();

            foreach (var entry in _queries)
            {
                string key = entry.Key;
                string query = entry.Value;

                DataTable queryResult = connectionManager.QueryService.ExecuteTable(query);

                // Extract the first row and first column value if present
                string result = queryResult.Rows.Count > 0
                    ? queryResult.Rows[0][0]?.ToString() ?? "NULL"
                    : " ";

                // Add the result to the dictionary
                results.Add(key, result);
            }


            Console.WriteLine(MarkdownFormatter.ConvertDictionaryToMarkdownTable(results, "Information", "Value"));

        }
    }
}
