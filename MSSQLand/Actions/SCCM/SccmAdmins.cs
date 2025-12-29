using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.SCCM
{
    /// <summary>
    /// Enumerate SCCM RBAC administrators.
    /// </summary>
    internal class SccmAdmins : BaseAction
    {
        public override void ValidateArguments(string[] args)
        {
            // No additional arguments needed
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Enumerating SCCM RBAC administrators");

            try
            {
                // Get and validate SCCM databases
                string[] requiredTables = { "RBAC_Admins" };
                var databases = databaseContext.SccmService.GetValidatedSccmDatabases(requiredTables, 1);
                
                if (databases.Count == 0)
                {
                    Logger.Warning("No valid SCCM databases found");
                    return null;
                }

                // Process each validated SCCM database
                foreach (string sccmDatabase in databases)
                {
                    string siteCode = SccmService.GetSiteCode(sccmDatabase);
                    Logger.Info($"Enumerating RBAC admins from: {sccmDatabase} (Site Code: {siteCode})");
                    Logger.NewLine();

                    // Get SCCM RBAC administrators
                    string rbacAdminsQuery = $@"
SELECT *
FROM [{sccmDatabase}].dbo.RBAC_Admins
ORDER BY CreatedDate DESC;
";

                    var rbacAdmins = databaseContext.QueryService.ExecuteTable(rbacAdminsQuery);
                    
                    if (rbacAdmins.Rows.Count > 0)
                    {
                        Logger.Success($"SCCM RBAC Administrators ({rbacAdmins.Rows.Count} total)");
                        Console.WriteLine(OutputFormatter.ConvertDataTable(rbacAdmins));
                    }
                    else
                    {
                        Logger.Info("No RBAC administrators found");
                    }
                }

                Logger.NewLine();
                Logger.Success($"Successfully enumerated RBAC admins from {databases.Count} SCCM database(s)");

                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to enumerate SCCM RBAC administrators: {ex.Message}");
                Logger.Debug($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }
    }
}
