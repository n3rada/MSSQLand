using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.ConfigMgr
{
    /// <summary>
    /// Display an overview of all ConfigMgr deployment types.
    /// Shows key metadata without large XML columns, ordered by modification/creation date.
    /// 
    /// Use Case:
    /// When you need to see all deployment types at a glance - what's been recently modified,
    /// what's enabled/disabled, who created what, and basic versioning information. Useful for
    /// auditing, change tracking, or finding deployment types to investigate further with cm-dt.
    /// 
    /// Information Displayed:
    /// - CI_ID (use with cm-dt for detailed analysis)
    /// - Title and localized version
    /// - Enabled/Expired/Hidden status
    /// - Creation and modification timestamps with authors
    /// - Source site
    /// - CI Version (increments with each revision)
    /// 
    /// Ordering:
    /// - Primary: Most recently modified first (DateLastModified DESC)
    /// - Secondary: Most recently created first (DateCreated DESC)
    /// 
    /// Examples:
    /// cm-dts
    /// cm-dts --limit 100
    /// 
    /// Typical Workflow:
    /// 1. Run cm-dts to see all deployment types
    /// 2. Identify deployment types of interest by modification date or title
    /// 3. Note the CI_ID
    /// 4. Run cm-dt [CI_ID] for detailed technical information
    /// </summary>
    internal class CMDeploymentTypes : BaseAction
    {
        [ArgumentMetadata(LongName = "limit", Description = "Limit number of results (default: 100)")]
        private int _limit = 100;

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            string limitStr = GetNamedArgument(named, "l", null)
                           ?? GetNamedArgument(named, "limit", null);
            if (!string.IsNullOrEmpty(limitStr))
            {
                _limit = int.Parse(limitStr);
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Retrieving all deployment types");
            Logger.TaskNested($"Limit: {_limit}");

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

                string topClause = _limit > 0 ? $"TOP {_limit}" : "";

                // Query all deployment types, ordered by modification and creation date
                string query = $@"
SELECT {topClause}
    ci.CI_ID,
    ci.CI_UniqueID,
    ci.CIVersion,
    ci.IsEnabled,
    ci.IsExpired,
    ci.IsHidden,
    ci.DateCreated,
    ci.CreatedBy,
    ci.DateLastModified,
    ci.LastModifiedBy,
    ci.SourceSite,
    lcp.Title,
    lcp.Version AS LocalizedVersion
FROM [{db}].dbo.CI_ConfigurationItems ci
LEFT JOIN [{db}].dbo.CI_LocalizedCIClientProperties lcp ON ci.CI_ID = lcp.CI_ID AND lcp.LocaleID = 1033
WHERE ci.CIType_ID = 21
ORDER BY ci.DateLastModified DESC, ci.DateCreated DESC;";

                DataTable results = databaseContext.QueryService.ExecuteTable(query);

                if (results.Rows.Count == 0)
                {
                    Logger.Warning($"No deployment types found in {db}");
                    continue;
                }

                Console.WriteLine(OutputFormatter.ConvertDataTable(results));
                Logger.Success($"Found {results.Rows.Count} deployment type(s)");
            }

            return null;
        }
    }
}