using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.SCCM
{
    /// <summary>
    /// List Azure AD application configurations stored in SCCM.
    /// Shows tenant information, client IDs, and encrypted secret keys.
    /// Requires decryption on Management Point for actual secrets.
    /// </summary>
    internal class SccmAadApps : BaseAction
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

            SccmService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            string[] requiredTables = { "AAD_Application_Ex", "AAD_Tenant_Ex" };
            var databases = sccmService.GetValidatedSccmDatabases(requiredTables, 2);

            if (databases.Count == 0)
            {
                Logger.Warning("No SCCM databases found with Azure AD configuration");
                return null;
            }

            foreach (string db in databases)
            {
                string siteCode = SccmService.GetSiteCode(db);
                Logger.Info($"SCCM database: {db} (Site Code: {siteCode})");

                try
                {
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
                        Logger.InfoNested("Encrypted secrets found. Decrypt on Management Point with:");
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
