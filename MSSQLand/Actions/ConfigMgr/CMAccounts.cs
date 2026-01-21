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

                string query = $@"
SELECT *
FROM [{db}].dbo.SC_UserAccount
ORDER BY UserName;
";

                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result.Rows.Count == 0)
                {
                    Logger.Warning("No stored credentials found");
                    continue;
                }

                Console.WriteLine(OutputFormatter.ConvertDataTable(result));
                Logger.Success($"Found {result.Rows.Count} stored credential(s)");
            }

            return null;
        }
    }
}
