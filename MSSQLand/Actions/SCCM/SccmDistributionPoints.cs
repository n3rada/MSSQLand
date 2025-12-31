using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.SCCM
{
    /// <summary>
    /// Enumerate SCCM distribution points with their content shares and properties.
    /// Distribution points host the actual package/application content - prime targets for lateral movement.
    /// </summary>
    internal class SccmDistributionPoints : BaseAction
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
            Logger.TaskNested($"Enumerating SCCM distribution points{filterMsg}");

            SccmService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            string[] requiredTables = { "v_DistributionPoint" };
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

                string filterClause = string.IsNullOrEmpty(_filter)
                    ? ""
                    : $"WHERE ServerName LIKE '%{_filter}%'";

                string query = $@"
SELECT
    ServerName,
    NALPath,
    CASE 
        WHEN NALPath LIKE '%\\\\%' THEN 
            SUBSTRING(NALPath, CHARINDEX('\\\\', NALPath) + 2, 
                CASE 
                    WHEN CHARINDEX('\\', NALPath, CHARINDEX('\\\\', NALPath) + 2) > 0 
                    THEN CHARINDEX('\\', NALPath, CHARINDEX('\\\\', NALPath) + 2) - CHARINDEX('\\\\', NALPath) - 2
                    ELSE LEN(NALPath)
                END)
        ELSE NULL
    END AS ContentShare,
    SiteCode,
    IsPullDP,
    IsMulticast,
    IsPXE,
    DPType
FROM [{db}].dbo.v_DistributionPoint
{filterClause}
ORDER BY ServerName;
";

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
