// MSSQLand/Actions/ConfigMgr/CMAdmins.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.ConfigMgr
{
    /// <summary>
    /// Enumerate ConfigMgr Role-Based Access Control (RBAC) administrators with their assigned roles and scopes.
    /// Use this to identify privileged users who can manage ConfigMgr infrastructure, deploy applications, or execute scripts.
    /// Shows admin accounts, security roles (Full Administrator, Operations Administrator, etc.), and collection scopes.
    /// Essential for privilege escalation paths and understanding administrative boundaries.
    /// </summary>
    internal class CMAdmins : BaseAction
    {
        public override void ValidateArguments(string[] args)
        {
            // No additional arguments needed
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Enumerating ConfigMgr RBAC administrators");

            CMService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            try
            {
                // Get ConfigMgr databases
                var databases = sccmService.GetSccmDatabases();
                
                if (databases.Count == 0)
                {
                    Logger.Warning("No valid ConfigMgr databases found");
                    return null;
                }

                // Process each validated ConfigMgr database
                foreach (string sccmDatabase in databases)
                {
                    string siteCode = CMService.GetSiteCode(sccmDatabase);
                    Logger.NewLine();
                    Logger.Info($"Enumerating RBAC admins from: {sccmDatabase} (Site Code: {siteCode})");

                    // Get ConfigMgr RBAC administrators
                    string rbacAdminsQuery = $@"
SELECT *
FROM [{sccmDatabase}].dbo.RBAC_Admins
ORDER BY CreatedDate DESC;
";

                    var rbacAdmins = databaseContext.QueryService.ExecuteTable(rbacAdminsQuery);
                    
                    if (rbacAdmins.Rows.Count > 0)
                    {
                        Logger.Success($"ConfigMgr RBAC Administrators ({rbacAdmins.Rows.Count} total)");
                        Console.WriteLine(OutputFormatter.ConvertDataTable(rbacAdmins));
                    }
                    else
                    {
                        Logger.Info("No RBAC administrators found");
                    }
                }

                Logger.NewLine();
                Logger.Success($"Successfully enumerated RBAC admins from {databases.Count} ConfigMgr database(s)");

                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to enumerate ConfigMgr RBAC administrators: {ex.Message}");
                Logger.Debug($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }
    }
}
