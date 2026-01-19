// MSSQLand/Actions/ConfigMgr/CMAccounts.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.ConfigMgr
{
    internal class CMAccounts : BaseAction
    {
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Enumerating ConfigMgr stored credentials");

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

                EnumerateCredentials(databaseContext, db, siteCode);
            }

            return null;
        }

        private void EnumerateCredentials(DatabaseContext databaseContext, string db, string siteCode)
        {
            // Try to identify account types from site control configuration
            string query = $@"
;WITH AccountUsage AS (
    SELECT DISTINCT 
        pl.Value AS UserName,
        CASE 
            WHEN pl.Name LIKE '%NAL%' OR pl.Name LIKE '%Network%Access%' THEN 'NAA'
            WHEN pl.Name LIKE '%Push%' OR sci.ItemName LIKE '%Push%' THEN 'Client Push'
            ELSE 'Other'
        END AS AccountType
    FROM [{db}].dbo.SC_SiteControlItem sci
    INNER JOIN [{db}].dbo.SC_SiteControlItem_Property pl 
        ON sci.ID = pl.ID AND sci.SiteNumber = pl.SiteNumber
    WHERE pl.Name LIKE '%Account%' 
        AND pl.Value LIKE '%\%' ESCAPE '\'
)
SELECT
    ua.UserName,
    ISNULL(au.AccountType, 'Unknown') AS Type,
    CASE ua.Availability
        WHEN 0 THEN 'Available'
        WHEN 1 THEN 'Unavailable'
        ELSE CAST(ua.Availability AS VARCHAR)
    END AS Status,
    sd.SiteCode,
    sd.SiteServerName
FROM [{db}].dbo.SC_UserAccount ua
LEFT JOIN [{db}].dbo.SC_SiteDefinition sd ON ua.SiteNumber = sd.SiteNumber
LEFT JOIN AccountUsage au ON ua.UserName = au.UserName
ORDER BY au.AccountType, ua.UserName;
";

            DataTable result;
            
            try
            {
                result = databaseContext.QueryService.ExecuteTable(query);
            }
            catch
            {
                // Fallback to simple query if property table doesn't exist
                string fallbackQuery = $@"
SELECT
    ua.UserName,
    CASE ua.Availability
        WHEN 0 THEN 'Available'
        WHEN 1 THEN 'Unavailable'
        ELSE CAST(ua.Availability AS VARCHAR)
    END AS Status,
    sd.SiteCode,
    sd.SiteServerName
FROM [{db}].dbo.SC_UserAccount ua
LEFT JOIN [{db}].dbo.SC_SiteDefinition sd ON ua.SiteNumber = sd.SiteNumber
ORDER BY ua.UserName;
";
                result = databaseContext.QueryService.ExecuteTable(fallbackQuery);
            }

            if (result.Rows.Count == 0)
            {
                Logger.Warning("No stored credentials found");
                return;
            }

            Console.WriteLine(OutputFormatter.ConvertDataTable(result));
            Logger.Success($"Found {result.Rows.Count} stored credential(s)");
        }
    }
}
