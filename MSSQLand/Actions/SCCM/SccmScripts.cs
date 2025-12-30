using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.SCCM
{
    internal class SccmScripts : BaseAction
    {
        public override void ValidateArguments(string[] args)
        {
            // No arguments required
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Enumerating SCCM scripts");

            SccmService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            string[] requiredTables = { "Scripts" };
            var databases = sccmService.GetValidatedSccmDatabases(requiredTables, 1);

            if (databases.Count == 0)
            {
                Logger.Warning("No SCCM databases found");
                return null;
            }

            foreach (string db in databases)
            {
                string siteCode = SccmService.GetSiteCode(db);

                Logger.NewLine();
                Logger.Info($"SCCM database: {db} (Site Code: {siteCode})");

                string query = $@"
SELECT
    ScriptGuid,
    ScriptName,
    ScriptDescription,
    Author,
    CAST(Script AS NVARCHAR(MAX)) AS Script,
    LastUpdateTime
FROM [CM_<SiteCode>].dbo.Scripts
ORDER BY LastUpdateTime DESC;
";

                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result.Rows.Count == 0)
                {
                    Logger.Warning("No scripts found");
                    continue;
                }

                Console.WriteLine(OutputFormatter.ConvertDataTable(result));
            }

            Logger.Success("Script enumeration completed");
            return null;
        }
    }
}
