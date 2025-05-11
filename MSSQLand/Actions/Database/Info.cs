using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Data;


namespace MSSQLand.Actions.Database
{
    /// <summary>
    /// Retrieving information of current DBMS server.
    /// </summary>
    internal class Info : BaseAction
    {
        [ExcludeFromArguments]
        private readonly Dictionary<string, string> _queries = new()
        {
            { "Server Name", "SELECT @@SERVERNAME;" },
            { "Default Domain", "SELECT DEFAULT_DOMAIN();" },
            { "SQL Service Process ID", "SELECT SERVERPROPERTY('ProcessId');" },
            { "Machine Type", @"DECLARE @MachineType NVARCHAR(255);  
            EXEC master.dbo.xp_instance_regread  
                N'HKEY_LOCAL_MACHINE', 
                N'SYSTEM\CurrentControlSet\Control\ProductOptions', 
                N'ProductType', 
                @MachineType OUTPUT;  
            SELECT @MachineType;
            " },
            { "Operating System Version", "DECLARE @ProductName  SYSNAME EXECUTE master.dbo.xp_regread @rootkey = N'HKEY_LOCAL_MACHINE', @key = N'SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion', @value_name = N'ProductName', @value = @ProductName output SELECT @ProductName;" },
            { "SQL Service Name", "SELECT SERVERPROPERTY('InstanceName') AS InstanceName;" },
            { "SQL Service Account Name", "EXEC master.dbo.xp_instance_regread N'HKEY_LOCAL_MACHINE', 'SYSTEM\\CurrentControlSet\\Services\\MSSQLSERVER', 'ObjectName';" },
            { "Authentication Mode", "SELECT CASE SERVERPROPERTY('IsIntegratedSecurityOnly') WHEN 1 THEN 'Windows Authentication' ELSE 'Mixed Authentication' END;" },
            { "Audit Enabled", "SELECT value_in_use FROM sys.configurations WHERE name = 'audit enabled';" },
            { "Encryption Enforced", "BEGIN TRY DECLARE @ForcedEncryption INT EXEC master.dbo.xp_instance_regread N'HKEY_LOCAL_MACHINE', N'SOFTWARE\\MICROSOFT\\Microsoft SQL Server\\MSSQLServer\\SuperSocketNetLib', N'ForceEncryption', @ForcedEncryption OUTPUT END TRY BEGIN CATCH END CATCH SELECT @ForcedEncryption;" },
            { "Clustered Server", "SELECT CASE SERVERPROPERTY('IsClustered') WHEN 0 THEN 'No' ELSE 'Yes' END;" },
            { "SQL Version", "SELECT SERVERPROPERTY('ProductVersion');" },
            { "SQL Major Version", "SELECT SUBSTRING(@@VERSION, CHARINDEX('2', @@VERSION), 4);" },
            { "SQL Edition", "SELECT SERVERPROPERTY('Edition');" },
            { "SQL Service Pack", "SELECT SERVERPROPERTY('ProductLevel');" },
            { "OS Architecture", "SELECT SUBSTRING(@@VERSION, CHARINDEX('x', @@VERSION), 3);" },
            { "OS Version Number", "SELECT RIGHT(SUBSTRING(@@VERSION, CHARINDEX('Windows Server', @@VERSION), 19), 4);" },
            { "Logged-in User", "SELECT SYSTEM_USER;" },
            { "Active SQL Sessions", "SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE status = 'running';" }
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
