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
            // Query accounts with their usage context from SC_UserAccount_Property
            // and type identification from SC_SiteControlItem
            string query = $@"
WITH AccountTypes AS (
    SELECT DISTINCT 
        pl.Value AS UserName,
        CASE 
            WHEN sci.ItemName = 'SMS_NETWORK_ACCESS_ACCOUNT' THEN 'NAA'
            WHEN sci.ItemName LIKE '%CLIENT_CONFIG_MANAGER%' OR sci.ItemName LIKE '%Push%' THEN 'Client Push'
            WHEN sci.ItemName LIKE '%SITE_COMPONENT_MANAGER%' THEN 'Site Component'
            WHEN pl.Name LIKE '%NAL%' OR pl.Name LIKE '%Network%Access%' THEN 'NAA'
            ELSE sci.ItemName
        END AS AccountType
    FROM [{db}].dbo.SC_SiteControlItem sci
    INNER JOIN [{db}].dbo.SC_SiteControlItem_Property pl 
        ON sci.ID = pl.ID AND sci.SiteNumber = pl.SiteNumber
    WHERE pl.Value LIKE '%\%' ESCAPE '\'
),
AccountUsage AS (
    SELECT 
        uap.UserAccountID,
        STRING_AGG(uap.Name + ': ' + ISNULL(uap.Value1, ''), ' | ') AS UsedFor
    FROM [{db}].dbo.SC_UserAccount_Property uap
    GROUP BY uap.UserAccountID
)
SELECT
    ua.UserName,
    COALESCE(at.AccountType, 
        CASE 
            WHEN ua.UserName LIKE '.\%' THEN 'Local Account'
            WHEN au.UsedFor LIKE '%SqlServer%' THEN 'SQL Connection'
            WHEN au.UsedFor LIKE '%FileShare%' THEN 'File Share'
            ELSE 'Service Account'
        END
    ) AS Type,
    ISNULL(au.UsedFor, '') AS UsedFor,
    CASE ua.Availability
        WHEN 0 THEN 'Available'
        WHEN 1 THEN 'Unavailable'
        ELSE CAST(ua.Availability AS VARCHAR)
    END AS Status,
    sd.SiteCode,
    sd.SiteServerName
FROM [{db}].dbo.SC_UserAccount ua
LEFT JOIN [{db}].dbo.SC_SiteDefinition sd ON ua.SiteNumber = sd.SiteNumber
LEFT JOIN AccountTypes at ON ua.UserName = at.UserName
LEFT JOIN AccountUsage au ON ua.ID = au.UserAccountID
ORDER BY at.AccountType, ua.UserName;
";

            DataTable result;
            
            try
            {
                result = databaseContext.QueryService.ExecuteTable(query);
            }
            catch
            {
                // Fallback for older SQL versions without STRING_AGG
                string fallbackQuery = $@"
SELECT
    ua.UserName,
    CASE 
        WHEN ua.UserName LIKE '.\%' THEN 'Local Account'
        WHEN ua.UserName LIKE '%naa%' OR ua.UserName LIKE '%network%' THEN 'NAA'
        WHEN ua.UserName LIKE '%push%' THEN 'Client Push'
        ELSE 'Service Account'
    END AS Type,
    ISNULL(uap.Name + ': ' + uap.Value1, '') AS UsedFor,
    CASE ua.Availability
        WHEN 0 THEN 'Available'
        WHEN 1 THEN 'Unavailable'
        ELSE CAST(ua.Availability AS VARCHAR)
    END AS Status,
    sd.SiteCode,
    sd.SiteServerName
FROM [{db}].dbo.SC_UserAccount ua
LEFT JOIN [{db}].dbo.SC_SiteDefinition sd ON ua.SiteNumber = sd.SiteNumber
LEFT JOIN [{db}].dbo.SC_UserAccount_Property uap ON ua.ID = uap.UserAccountID
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
