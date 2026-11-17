// MSSQLand/Actions/Cloud/Azure/AzureUsers.cs

using System;
using System.Data;

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;

namespace MSSQLand.Actions.Cloud.Azure
{
    /// <summary>
    /// Enumerate contained database users, Entra ID (Azure AD) principals,
    /// and their database role memberships in Azure SQL Database.
    /// </summary>
    internal class AzureUsers : BaseAction
    {
        public override object Execute(DatabaseContext databaseContext)
        {
            if (!AzureFirewall.RequiresAzureSQL(databaseContext))
            {
                return null;
            }

            QueryService qs = databaseContext.QueryService;

            ContainedPrincipals(qs);
            RoleMemberships(qs);

            return true;
        }

        private static void ContainedPrincipals(QueryService qs)
        {
            string query = @"
                SELECT
                    dp.name AS [Principal],
                    dp.type_desc AS [Type],
                    dp.authentication_type_desc AS [Auth Type],
                    dp.default_schema_name AS [Default Schema],
                    dp.create_date AS [Created],
                    dp.modify_date AS [Modified],
                    CASE
                        WHEN dp.type IN ('E', 'X') THEN 'Entra ID'
                        WHEN dp.authentication_type_desc = 'DATABASE' THEN 'Contained SQL'
                        WHEN dp.authentication_type_desc = 'WINDOWS' THEN 'Windows'
                        ELSE dp.authentication_type_desc
                    END AS [Auth Source]
                FROM sys.database_principals dp
                WHERE dp.type IN ('S', 'U', 'G', 'E', 'X')
                  AND dp.sid IS NOT NULL
                  AND dp.name NOT LIKE '##%'
                  AND dp.name NOT IN ('dbo', 'guest', 'INFORMATION_SCHEMA', 'sys')
                ORDER BY dp.type_desc, dp.name;";

            try
            {
                DataTable dt = qs.ExecuteTable(query);
                if (dt.Rows.Count == 0)
                {
                    Logger.Warning("No contained database principals found");
                    return;
                }

                // Count by auth source
                int entraCount = 0;
                int sqlCount = 0;
                foreach (DataRow row in dt.Rows)
                {
                    string authSource = row["Auth Source"]?.ToString() ?? "";
                    if (authSource == "Entra ID") entraCount++;
                    else if (authSource == "Contained SQL") sqlCount++;
                }

                int otherCount = dt.Rows.Count - entraCount - sqlCount;
                Logger.Success($"Found {dt.Rows.Count} principal(s): {entraCount} Entra ID, {sqlCount} contained SQL, {otherCount} other");
                Console.WriteLine(OutputFormatter.ConvertDataTable(dt));
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to enumerate contained principals: {ex.Message}");
            }
        }

        private static void RoleMemberships(QueryService qs)
        {
            string query = @"
                SELECT
                    dp.name AS [Principal],
                    dp.type_desc AS [Principal Type],
                    STRING_AGG(r.name, ', ') WITHIN GROUP (ORDER BY r.name) AS [Database Roles],
                    CASE
                        WHEN EXISTS(
                            SELECT 1 FROM sys.database_role_members drm2
                            INNER JOIN sys.database_principals r2 ON drm2.role_principal_id = r2.principal_id
                            WHERE drm2.member_principal_id = dp.principal_id
                              AND r2.name IN ('db_owner', 'db_securityadmin', 'db_accessadmin')
                        ) THEN 'Yes'
                        ELSE ''
                    END AS [Privileged]
                FROM sys.database_principals dp
                INNER JOIN sys.database_role_members drm
                    ON dp.principal_id = drm.member_principal_id
                INNER JOIN sys.database_principals r
                    ON drm.role_principal_id = r.principal_id
                WHERE dp.type IN ('S', 'U', 'G', 'E', 'X')
                  AND dp.name NOT LIKE '##%'
                  AND dp.name NOT IN ('dbo', 'guest', 'INFORMATION_SCHEMA', 'sys')
                GROUP BY dp.name, dp.type_desc, dp.principal_id
                ORDER BY dp.name;";

            try
            {
                DataTable dt = qs.ExecuteTable(query);
                if (dt.Rows.Count == 0)
                {
                    Logger.Debug("No explicit database role memberships found");
                    return;
                }

                Logger.Info($"Database role memberships ({dt.Rows.Count} principal(s))");
                Console.WriteLine(OutputFormatter.ConvertDataTable(dt));
            }
            catch (Exception ex)
            {
                Logger.Warning($"Could not enumerate role memberships: {ex.Message}");
            }
        }
    }
}
