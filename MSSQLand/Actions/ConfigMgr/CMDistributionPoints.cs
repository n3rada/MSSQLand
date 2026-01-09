using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.ConfigMgr
{
    /// <summary>
    /// Enumerate ConfigMgr distribution points with content library paths and network shares.
    /// Use this to identify servers hosting package content - primary targets for lateral movement and content poisoning.
    /// Shows DP server names, content share paths (e.g., \\\\server\\SCCMContentLib$), NAL paths, and DP group memberships.
    /// Distribution points store all deployed content and often have relaxed security for client access.
    /// Critical for content modification attacks and identifying high-value file shares.
    /// </summary>
    internal class CMDistributionPoints : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "f", LongName = "filter", Description = "Filter by DP name")]
        private string _filter = "";

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            _filter = GetNamedArgument(named, "f", null)
                   ?? GetNamedArgument(named, "filter", null)
                   ?? GetPositionalArgument(positional, 0, "");
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            string filterMsg = !string.IsNullOrEmpty(_filter) ? $" (filter: {_filter})" : "";
            Logger.TaskNested($"Enumerating ConfigMgr distribution points{filterMsg}");

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

                string filterClause = string.IsNullOrEmpty(_filter)
                    ? ""
                    : $"WHERE ServerNALPath LIKE '%{_filter}%'";

                string query = $@"SELECT * FROM [{db}].dbo.v_DistributionPoint {filterClause} ORDER BY ServerNALPath;";

                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result.Rows.Count == 0)
                {
                    Logger.Warning("No distribution points found");
                    continue;
                }

                Console.WriteLine(OutputFormatter.ConvertDataTable(result));

                Logger.Success($"Found {result.Rows.Count} distribution point(s)");
            }

            Logger.Success("Distribution point enumeration completed");
            return null;
        }
    }
}
