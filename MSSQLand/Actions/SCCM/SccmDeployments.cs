using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.SCCM
{
    /// <summary>
    /// Enumerate SCCM deployments including applications, packages, task sequences, and software updates.
    /// Shows what content is being deployed to which collections and deployment settings.
    /// </summary>
    internal class SccmDeployments : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "f", LongName = "filter", Description = "Filter by deployment name")]
        private string _filter = "";

        [ArgumentMetadata(Position = 1, ShortName = "l", LongName = "limit", Description = "Limit number of results (default: 100)")]
        private int _limit = 100;

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            _filter = GetNamedArgument(named, "f", null)
                   ?? GetNamedArgument(named, "filter", null)
                   ?? GetPositionalArgument(positional, 0, "");

            string limitStr = GetNamedArgument(named, "l", null)
                           ?? GetNamedArgument(named, "limit", null)
                           ?? GetPositionalArgument(positional, 1);
            if (!string.IsNullOrEmpty(limitStr))
            {
                _limit = int.Parse(limitStr);
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            string filterMsg = !string.IsNullOrEmpty(_filter) ? $" (filter: {_filter})" : "";
            Logger.TaskNested($"Enumerating SCCM deployments{filterMsg}");

            SccmService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            string[] requiredTables = { "vSMS_DeploymentSummary", "v_Collection" };
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
                    : $"WHERE ds.DeploymentName LIKE '%{_filter}%'";

                string query = $@"
SELECT TOP {_limit}
    ds.DeploymentID,
    ds.DeploymentName,
    ds.CollectionID,
    c.Name AS CollectionName,
    ds.FeatureType,
    CASE ds.FeatureType
        WHEN 1 THEN 'Application'
        WHEN 2 THEN 'Package'
        WHEN 3 THEN 'Software Update'
        WHEN 4 THEN 'Task Sequence'
        WHEN 5 THEN 'Device Setting'
        WHEN 7 THEN 'Baseline'
        ELSE CAST(ds.FeatureType AS VARCHAR)
    END AS DeploymentType,
    ds.DeploymentIntent,
    CASE ds.DeploymentIntent
        WHEN 1 THEN 'Required'
        WHEN 2 THEN 'Available'
        WHEN 3 THEN 'Simulate'
        ELSE CAST(ds.DeploymentIntent AS VARCHAR)
    END AS Intent,
    ds.NumberSuccess,
    ds.NumberInProgress,
    ds.NumberErrors,
    ds.NumberOther,
    ds.NumberUnknown,
    ds.DeploymentTime,
    ds.CreationTime,
    ds.ModificationTime
FROM [{db}].dbo.vSMS_DeploymentSummary ds
LEFT JOIN [{db}].dbo.v_Collection c ON ds.CollectionID = c.CollectionID
{filterClause}
ORDER BY ds.CreationTime DESC;
";

                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result.Rows.Count == 0)
                {
                    Logger.Warning("No deployments found");
                    continue;
                }

                Logger.Success($"Found {result.Rows.Count} deployment(s)");
                Console.WriteLine(OutputFormatter.ConvertDataTable(result));
            }

            Logger.Success("Deployment enumeration completed");
            return null;
        }
    }
}
