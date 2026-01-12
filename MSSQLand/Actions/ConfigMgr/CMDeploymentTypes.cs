using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Collections.Generic;
using System.Data;
using System.Xml;

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

                // Build WHERE clause with SQL-level filters (non-XML fields)
                string whereClause = "ci.CIType_ID = 21";
                if (!string.IsNullOrEmpty(_enabled))
                {
                    bool enabledFilter = bool.Parse(_enabled);
                    whereClause += $" AND ci.IsEnabled = {(enabledFilter ? "1" : "0")}";
                }

                string query = $@"
SELECT {topClause}
    ci.CI_ID,
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
    lcp.Version AS LocalizedVersion
FROM [{db}].dbo.CI_ConfigurationItems ci
LEFT JOIN [{db}].dbo.CI_LocalizedCIClientProperties lcp ON ci.CI_ID = lcp.CI_ID AND lcp.LocaleID = 1033
WHERE {whereClause}
ORDER BY ci.DateLastModified DESC, ci.DateCreated DESC;";

                DataTable results = databaseContext.QueryService.ExecuteTable(query);

                if (results.Rows.Count == 0)
                {
                    Logger.Warning($"No deployment types found in {db}");
                    continue;
                }

                // Parse SDMPackageDigest XML and add extracted columns
                int sdmIndex = results.Columns["SDMPackageDigest"]?.Ordinal ?? -1;
                if (sdmIndex >= 0)
                {
                    // Add new columns for parsed data
                    DataColumn technologyCol = results.Columns.Add("Technology", typeof(string));
                    DataColumn installCmdCol = results.Columns.Add("InstallCommand", typeof(string));
                    DataColumn contentCol = results.Columns.Add("ContentLocation", typeof(string));
                    DataColumn detectionCol = results.Columns.Add("DetectionType", typeof(string));
                    DataColumn executionCol = results.Columns.Add("ExecutionContext", typeof(string));

                    // Add ParentApplication column only if needed for filtering or display
                    DataColumn parentAppCol = null;
                    if (!string.IsNullOrEmpty(_application))
                    {
                        parentAppCol = results.Columns.Add("ParentApplication", typeof(string));
                        
                        // Fetch parent applications for filtering
                        string parentQuery = $@"
SELECT 
    rel.ToCI_ID,
    COALESCE(lp.DisplayName, lcp_app.Title) AS ApplicationName
FROM [{db}].dbo.CI_ConfigurationItemRelations rel
INNER JOIN [{db}].dbo.CI_ConfigurationItems ci_app ON rel.FromCI_ID = ci_app.CI_ID
LEFT JOIN [{db}].dbo.v_LocalizedCIProperties lp ON ci_app.CI_ID = lp.CI_ID AND lp.LocaleID = 1033
LEFT JOIN [{db}].dbo.CI_LocalizedCIClientProperties lcp_app ON ci_app.CI_ID = lcp_app.CI_ID AND lcp_app.LocaleID = 1033
WHERE rel.RelationType = 9";
                        
                        DataTable parentApps = databaseContext.QueryService.ExecuteTable(parentQuery);
                        Dictionary<int, string> parentAppLookup = new Dictionary<int, string>();
                        foreach (DataRow parentRow in parentApps.Rows)
                        {
                            int ciId = Convert.ToInt32(parentRow["ToCI_ID"]);
                            string appName = parentRow["ApplicationName"]?.ToString() ?? "";
                            parentAppLookup[ciId] = appName;
                        }
                        
                        // Populate ParentApplication column
                        foreach (DataRow row in results.Rows)
                        {
                            int ciId = Convert.ToInt32(row["CI_ID"]);
                            row["ParentApplication"] = parentAppLookup.ContainsKey(ciId) ? parentAppLookup[ciId] : "";
                        }
                    }

                    // Position new columns after Title
                    int titleIndex = results.Columns["Title"]?.Ordinal ?? 0;
                    technologyCol.SetOrdinal(titleIndex + 1);
                    installCmdCol.SetOrdinal(titleIndex + 2);
                    contentCol.SetOrdinal(titleIndex + 3);
                    detectionCol.SetOrdinal(titleIndex + 4);
                    executionCol.SetOrdinal(titleIndex + 5);
                    if (parentAppCol != null)
                        parentAppCol.SetOrdinal(titleIndex + 6);

                    // Parse XML for each row
                    List<DataRow> rowsToRemove = new List<DataRow>();
                    foreach (DataRow row in results.Rows)
                    {
                        ParseSDMPackageDigest(row);
                    }

                    // Remove SDMPackageDigest column immediately after parsing (huge XML blob)
                    results.Columns.Remove("SDMPackageDigest");

                    // Apply filters (before truncation so full values can be matched)
                    foreach (DataRow row in results.Rows)
                    {

                        bool keepRow = true;

                        if (!string.IsNullOrEmpty(_technology))
                        {
                            string tech = row["Technology"]?.ToString() ?? "";
                            if (tech.IndexOf(_technology, StringComparison.OrdinalIgnoreCase) < 0)
                                keepRow = false;
                        }

                        if (!string.IsNullOrEmpty(_contentPath))
                        {
                            string content = row["ContentLocation"]?.ToString() ?? "";
                            if (content.IndexOf(_contentPath, StringComparison.OrdinalIgnoreCase) < 0)
                                keepRow = false;
                        }

                        if (!string.IsNullOrEmpty(_installCommand))
                        {
                            string install = row["InstallCommand"]?.ToString() ?? "";
                            if (install.IndexOf(_installCommand, StringComparison.OrdinalIgnoreCase) < 0)
                                keepRow = false;
                        }

                        if (!string.IsNullOrEmpty(_executionContext))
                        {
                            string context = row["ExecutionContext"]?.ToString() ?? "";
                            if (context.IndexOf(_executionContext, StringComparison.OrdinalIgnoreCase) < 0)
                                keepRow = false;
                        }

                        if (!string.IsNullOrEmpty(_detectionType))
                        {
                            string detection = row["DetectionType"]?.ToString() ?? "";
                            if (detection.IndexOf(_detectionType, StringComparison.OrdinalIgnoreCase) < 0)
                                keepRow = false;
                        }

                        if (!string.IsNullOrEmpty(_application))
                        {
                            string app = row["ParentApplication"]?.ToString() ?? "";
                            if (app.IndexOf(_application, StringComparison.OrdinalIgnoreCase) < 0)
                                keepRow = false;
                        }

                        // Note: --enabled filter already applied at SQL level

                        if (!keepRow)
                            rowsToRemove.Add(row);
                    }

                    // Remove filtered rows
                    foreach (DataRow row in rowsToRemove)
                    {
                        results.Rows.Remove(row);
                    }

                    // Truncate long fields for display after filtering
                    foreach (DataRow row in results.Rows)
                    {
                        TruncateLongFields(row);
                    }
                }

                if (results.Rows.Count == 0)
                {
                    Logger.Warning($"No deployment types found matching the specified filters in {db}");
                    continue;
                }

                Console.WriteLine(OutputFormatter.ConvertDataTable(results));
                Logger.Success($"Found {results.Rows.Count} deployment type(s)");
            }

            return null;
        }

        /// <summary>
        /// Parse SDMPackageDigest XML and extract key fields
        /// </summary>
        private static void ParseSDMPackageDigest(DataRow row)
        {
            string xmlContent = row["SDMPackageDigest"]?.ToString() ?? "";
            
            if (string.IsNullOrEmpty(xmlContent))
            {
                row["Technology"] = "";
                row["InstallCommand"] = "";
                row["ContentLocation"] = "";
                row["DetectionType"] = "";
                row["ExecutionContext"] = "";
                return;
            }

            try
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xmlContent);

                XmlNamespaceManager nsmgr = new XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("dcm", "http://schemas.microsoft.com/SystemsManagementServer/2005/03/10/DesiredConfiguration");
                nsmgr.AddNamespace("xsi", "http://www.w3.org/2001/XMLSchema-instance");

                // Extract Technology
                XmlNode techNode = doc.SelectSingleNode("//dcm:DeploymentType/dcm:Installer/@Technology", nsmgr);
                row["Technology"] = techNode?.Value ?? "";

                // Extract Install Command
                XmlNode installNode = doc.SelectSingleNode("//dcm:InstallAction/dcm:Provider/dcm:Data[@id='InstallCommandLine']", nsmgr);
                row["InstallCommand"] = installNode?.InnerText ?? "";

                // Extract Content Location
                XmlNode contentNode = doc.SelectSingleNode("//dcm:ContentRef/dcm:Location", nsmgr);
                row["ContentLocation"] = contentNode?.InnerText ?? "";

                // Extract Detection Type
                XmlNode detectionNode = doc.SelectSingleNode("//dcm:EnhancedDetectionMethod/@DataType", nsmgr);
                if (detectionNode == null)
                    detectionNode = doc.SelectSingleNode("//dcm:DetectAction/dcm:Provider/@DataType", nsmgr);
                row["DetectionType"] = detectionNode?.Value ?? "";

                // Extract Execution Context
                XmlNode contextNode = doc.SelectSingleNode("//dcm:InstallAction/dcm:Provider/dcm:Data[@id='ExecutionContext']", nsmgr);
                row["ExecutionContext"] = contextNode?.InnerText ?? "";
            }
            catch (Exception)
            {
                // If XML parsing fails, set empty values
                row["Technology"] = "";
                row["InstallCommand"] = "";
                row["ContentLocation"] = "";
                row["DetectionType"] = "";
                row["ExecutionContext"] = "";
            }
        }

        /// <summary>
        /// Truncate long fields to keep output compact
        /// </summary>
        private static void TruncateLongFields(DataRow row)
        {
            const int maxInstallCmdLength = 150;
            const int maxContentLength = 100;

            // Truncate InstallCommand if too long
            string installCmd = row["InstallCommand"]?.ToString() ?? "";
            if (installCmd.Length > maxInstallCmdLength)
            {
                row["InstallCommand"] = installCmd.Substring(0, maxInstallCmdLength) + "...";
            }

            // Truncate ContentLocation if too long
            string content = row["ContentLocation"]?.ToString() ?? "";
            if (content.Length > maxContentLength)
            {
                row["ContentLocation"] = content.Substring(0, maxContentLength) + "...";
            }
        }
    }
}