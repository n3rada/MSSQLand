// MSSQLand/Actions/ConfigMgr/CMServers.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.ConfigMgr
{
    /// <summary>
    /// Enumerate ConfigMgr servers in the site hierarchy including site servers, management points, and distribution points.
    /// Use this to map the ConfigMgr infrastructure and identify key servers for lateral movement targets.
    /// Shows site codes, server roles, database servers, and installation paths.
    /// Essential for understanding the ConfigMgr topology and identifying high-value targets.
    /// </summary>
    internal class CMServers : BaseAction
    {
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Enumerating ConfigMgr servers in hierarchy");

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
                Logger.Info($"ConfigMgr database: {db} (Site Code: {siteCode})");

                try
                {
                    string query = $@"
SELECT DISTINCT
    sd.SiteCode,
    sd.SiteServerName,
    sd.SiteDatabaseName,
    sd.SiteDatabaseServer,
    CASE sd.SiteType
        WHEN 1 THEN 'Secondary Site'
        WHEN 2 THEN 'Primary Site'
        WHEN 4 THEN 'Central Administration Site'
        ELSE 'Unknown'
    END AS SiteType,
    s.NALPath,
    s.RoleName
FROM [{db}].dbo.SC_SiteDefinition sd
LEFT JOIN [{db}].dbo.ServerData s ON sd.SiteCode = s.SiteCode
WHERE sd.SiteServerName IS NOT NULL
ORDER BY sd.SiteCode, sd.SiteServerName";

                    DataTable serversTable = databaseContext.QueryService.ExecuteTable(query);

                    if (serversTable.Rows.Count == 0)
                    {
                        Logger.Warning("No servers found");
                        continue;
                    }

                    Logger.Success($"Found {serversTable.Rows.Count} server(s)");
                    Logger.NewLine();

                    Console.WriteLine(OutputFormatter.ConvertDataTable(serversTable));
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to enumerate servers: {ex.Message}");
                }
            }

            return null;
        }
    }
}
