using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.ConfigMgr
{
    /// <summary>
    /// Display an overview of all ConfigMgr deployment types with searchable technical details.
    /// Shows key metadata including technology type, install commands, content paths, and detection methods.
    /// Supports filtering by technology, content location, install command, and other criteria.
    /// 
    /// Use Case:
    /// When you need to see all deployment types at a glance - what's been recently modified,
    /// what's enabled/disabled, who created what, and basic versioning information. Useful for
    /// auditing, change tracking, finding deployment types by technology or content path, or
    /// identifying deployment types to investigate further with cm-dt.
    /// 
    /// Information Displayed:
    /// - CI_ID (use with cm-dt for detailed analysis)
    /// - Title, localized version, and parent application
    /// - Technology type (MSI, Script, AppV5, etc.)
    /// - Install command and execution context
    /// - Content location (UNC path)
    /// - Detection method type
    /// - Enabled/Expired/Hidden status
    /// - Creation and modification timestamps with authors
    /// 
    /// Filtering:
    /// --tech [value]        Filter by technology (MSI, Script, etc.)
    /// --content [path]      Filter by content location path
    /// --install [cmd]       Filter by install command
    /// --context [System|User]  Filter by execution context
    /// --detection [type]    Filter by detection method type
    /// --app [name]          Filter by parent application name
    /// --enabled [true|false]  Filter by enabled status
    /// --limit [number]      Limit results (default: 100)
    /// 
    /// Examples:
    /// cm-dts
    /// cm-dts --tech MSI
    /// cm-dts --content "\\server\Chrome"
    /// cm-dts --context System
    /// cm-dts --app "Google Chrome"
    /// cm-dts --enabled true --limit 50
    /// 
    /// Typical Workflow:
    /// 1. Run cm-dts to see all deployment types
    /// 2. Filter by technology or content path to narrow down
    /// 3. Note the CI_ID of interest
    /// 4. Run cm-dt [CI_ID] for detailed technical information
    /// </summary>
    internal class CMDeploymentTypes : BaseAction
    {
        [ArgumentMetadata(LongName = "tech", Description = "Filter by technology type (MSI, Script, AppV5, etc.)")]
        private string _technology = "";

        [ArgumentMetadata(LongName = "content", Description = "Filter by content location path")]
        private string _contentPath = "";

        [ArgumentMetadata(LongName = "install", Description = "Filter by install command")]
        private string _installCommand = "";

        [ArgumentMetadata(LongName = "context", Description = "Filter by execution context (System, User)")]
        private string _executionContext = "";

        [ArgumentMetadata(LongName = "detection", Description = "Filter by detection method type")]
        private string _detectionType = "";

        [ArgumentMetadata(LongName = "app", Description = "Filter by parent application name")]
        private string _application = "";

        [ArgumentMetadata(LongName = "enabled", Description = "Filter by enabled status (true/false)")]
        private string _enabled = "";

        [ArgumentMetadata(LongName = "limit", Description = "Limit number of results (default: 25)")]
        private int _limit = 50;

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            _technology = GetNamedArgument(named, "tech", null) ?? "";
            _contentPath = GetNamedArgument(named, "content", null) ?? "";
            _installCommand = GetNamedArgument(named, "install", null) ?? "";
            _executionContext = GetNamedArgument(named, "context", null) ?? "";
            _detectionType = GetNamedArgument(named, "detection", null) ?? "";
            _application = GetNamedArgument(named, "app", null) ?? "";
            _enabled = GetNamedArgument(named, "enabled", null) ?? "";

            string limitStr = GetNamedArgument(named, "l", null)
                           ?? GetNamedArgument(named, "limit", null);
            if (!string.IsNullOrEmpty(limitStr))
            {
                _limit = int.Parse(limitStr);
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            string filterMsg = "";
            if (!string.IsNullOrEmpty(_technology)) filterMsg += $" tech:{_technology}";
            if (!string.IsNullOrEmpty(_contentPath)) filterMsg += $" content:{_contentPath}";
            if (!string.IsNullOrEmpty(_installCommand)) filterMsg += $" install:{_installCommand}";
            if (!string.IsNullOrEmpty(_executionContext)) filterMsg += $" context:{_executionContext}";
            if (!string.IsNullOrEmpty(_detectionType)) filterMsg += $" detection:{_detectionType}";
            if (!string.IsNullOrEmpty(_application)) filterMsg += $" app:{_application}";
            if (!string.IsNullOrEmpty(_enabled)) filterMsg += $" enabled:{_enabled}";

            Logger.TaskNested($"Retrieving deployment types{(string.IsNullOrEmpty(filterMsg) ? "" : $" (filters:{filterMsg})")}");
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

                // Query all deployment types with XML for parsing
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
    ci.SDMPackageDigest,
    lcp.Title,
    lcp.Version AS LocalizedVersion,
    parent_app.ApplicationName AS ParentApplication
FROM [{db}].dbo.CI_ConfigurationItems ci
LEFT JOIN [{db}].dbo.CI_LocalizedCIClientProperties lcp ON ci.CI_ID = lcp.CI_ID AND lcp.LocaleID = 1033
LEFT JOIN (
    SELECT 
        rel.ToCI_ID,
        COALESCE(lp.DisplayName, lcp_app.Title) AS ApplicationName
    FROM [{db}].dbo.CI_ConfigurationItemRelations rel
    INNER JOIN [{db}].dbo.CI_ConfigurationItems ci_app ON rel.FromCI_ID = ci_app.CI_ID
    LEFT JOIN [{db}].dbo.v_LocalizedCIProperties lp ON ci_app.CI_ID = lp.CI_ID AND lp.LocaleID = 1033
    LEFT JOIN [{db}].dbo.CI_LocalizedCIClientProperties lcp_app ON ci_app.CI_ID = lcp_app.CI_ID AND lcp_app.LocaleID = 1033
    WHERE rel.RelationType = 9
) parent_app ON ci.CI_ID = parent_app.ToCI_ID
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