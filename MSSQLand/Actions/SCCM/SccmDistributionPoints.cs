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
                    : $"WHERE dp.ServerName LIKE '%{_filter}%'";

                string query = $@"
SELECT DISTINCT
    dp.ServerName,
    dp.NALPath,
    CASE 
        WHEN dp.NALPath LIKE '%\\\\%' THEN 
            SUBSTRING(dp.NALPath, CHARINDEX('\\\\', dp.NALPath) + 2, 
                CASE 
                    WHEN CHARINDEX('\\', dp.NALPath, CHARINDEX('\\\\', dp.NALPath) + 2) > 0 
                    THEN CHARINDEX('\\', dp.NALPath, CHARINDEX('\\\\', dp.NALPath) + 2) - CHARINDEX('\\\\', dp.NALPath) - 2
                    ELSE LEN(dp.NALPath)
                END)
        ELSE NULL
    END AS ContentShare,
    dp.SiteCode,
    dp.IsPullDP,
    dp.IsMulticast,
    dp.IsPXE,
    CASE dp.DPType
        WHEN 0 THEN 'Distribution Point'
        WHEN 1 THEN 'Pull Distribution Point'
        ELSE CAST(dp.DPType AS VARCHAR)
    END AS DPType,
    COUNT(DISTINCT dps.PackageID) AS PackageCount
FROM [{db}].dbo.v_DistributionPoint dp
LEFT JOIN [{db}].dbo.v_DistributionPointStatus dps ON dp.ServerName = dps.ServerName
{filterClause}
GROUP BY 
    dp.ServerName, dp.NALPath, dp.SiteCode, dp.IsPullDP, 
    dp.IsMulticast, dp.IsPXE, dp.DPType
ORDER BY dp.ServerName;
";

                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result.Rows.Count == 0)
                {
                    Logger.Warning("No distribution points found");
                    continue;
                }

                Logger.Success($"Found {result.Rows.Count} distribution point(s)");
                Console.WriteLine(OutputFormatter.ConvertDataTable(result));
            }

            Logger.Success("Distribution point enumeration completed");
            return null;
        }
    }
}
