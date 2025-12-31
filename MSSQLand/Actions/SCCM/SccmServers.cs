using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.SCCM
{
    /// <summary>
    /// List SCCM servers in the hierarchy with associated database server and site code.
    /// Queries SC_SiteDefinition and ServerData tables.
    /// </summary>
    internal class SccmServers : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "f", LongName = "format", Description = "Output format (table, csv, markdown)")]
        private string _format = "table";

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            _format = GetNamedArgument(named, "f", null)
                   ?? GetNamedArgument(named, "format", null)
                   ?? GetPositionalArgument(positional, 0, "table");
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Enumerating SCCM servers in hierarchy");

            SccmService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            string[] requiredTables = { "SC_SiteDefinition", "ServerData" };
            var databases = sccmService.GetValidatedSccmDatabases(requiredTables, 2);

            if (databases.Count == 0)
            {
                Logger.Warning("No SCCM databases found");
                return null;
            }

            foreach (string db in databases)
            {
                string siteCode = SccmService.GetSiteCode(db);
                Logger.Info($"SCCM database: {db} (Site Code: {siteCode})");

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

                    IOutputFormatter formatter = OutputFormatterFactory.GetFormatter(_format);
                    Console.WriteLine(formatter.ConvertDataTable(serversTable));
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
