using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.ConfigMgr
{
    /// <summary>
    /// Trace a deployment type GUID from client logs (AppDiscovery.log, AppEnforce.log) back to its assignments and collections.
    /// Takes a ScopeId/DeploymentType GUID from logs and follows the relationship chain:
    /// Log GUID → Document → CI → Parent Application → Assignments → Collections
    /// Use this to understand which deployments are causing software to be installed/reinstalled.
    /// </summary>
    internal class CMLogTrace : BaseAction
    {
        [ArgumentMetadata(Position = 0, Description = "Deployment Type GUID from log (e.g., ScopeId_xxx/DeploymentType_xxx or just the GUID portion)")]
        private string _guid = "";

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            _guid = GetPositionalArgument(positional, 0, "")
                 ?? GetNamedArgument(named, "guid", null)
                 ?? GetNamedArgument(named, "g", null)
                 ?? "";

            if (string.IsNullOrWhiteSpace(_guid))
            {
                throw new ArgumentException("Deployment Type GUID is required. Example: ScopeId_xxx/DeploymentType_xxx or just DeploymentType_xxx");
            }

            // Clean up the GUID - remove any prefix/suffix if user pasted full log line
            _guid = _guid.Trim();
            
            // Extract just the GUID portion if it contains ScopeId
            if (_guid.Contains("ScopeId_"))
            {
                // Already looks like a full identifier, keep it
            }
            else if (_guid.StartsWith("DeploymentType_", StringComparison.OrdinalIgnoreCase))
            {
                // User provided just DeploymentType_xxx, that's fine
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(_guid, @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                // User provided just the GUID without DeploymentType_ prefix
                _guid = "DeploymentType_" + _guid;
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Tracing deployment type: {_guid}");

            CMService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            var databases = sccmService.GetSccmDatabases();

            if (databases.Count == 0)
            {
                Logger.Warning("No ConfigMgr databases found");
                return null;
            }

            bool found = false;

            foreach (string db in databases)
            {
                string siteCode = CMService.GetSiteCode(db);

                Logger.NewLine();
                Logger.Info($"ConfigMgr database: {db} (Site Code: {siteCode})");

                // Step 1: Find Document_ID in CI_DocumentStore
                Logger.NewLine();
                Logger.Info("Looking up deployment type document");

                string documentQuery = $@"
SELECT TOP 1 *
FROM [{db}].dbo.CI_DocumentStore
WHERE DocumentIdentifier LIKE '%{_guid.Replace("'", "''")}%' AND IsVersionLatest = 1
ORDER BY Document_ID DESC";

                DataTable documentResult = databaseContext.QueryService.ExecuteTable(documentQuery);

                if (documentResult.Rows.Count == 0)
                {
                    Logger.Warning($"No document found matching GUID: {_guid}");
                    continue;
                }

                found = true;
                int documentId = Convert.ToInt32(documentResult.Rows[0]["Document_ID"]);
                string fullIdentifier = documentResult.Rows[0]["DocumentIdentifier"].ToString();
                int documentType = Convert.ToInt32(documentResult.Rows[0]["DocumentType"]);

                Logger.Success($"Found Document_ID: {documentId}");
                Logger.SuccessNested($"Document Identifier: {fullIdentifier}");
                Logger.SuccessNested($"Document Type: {documentType}");

                // Step 2: Find CI_ID from CI_CIDocuments
                Logger.NewLine();
                string ciQuery = $@"SELECT CI_ID FROM [{db}].dbo.CI_CIDocuments WHERE Document_ID = {documentId}";

                DataTable ciResult = databaseContext.QueryService.ExecuteTable(ciQuery);

                if (ciResult.Rows.Count == 0)
                {
                    Logger.Warning("No CI_ID found for this document");
                    continue;
                }

                int deploymentTypeCiId = Convert.ToInt32(ciResult.Rows[0]["CI_ID"]);
                Logger.Success($"Deployment Type CI_ID: {deploymentTypeCiId}");

                // Get deployment type details
                string dtDetailsQuery = $@"
SELECT * 
FROM [{db}].dbo.CI_ConfigurationItems ci
LEFT JOIN [{db}].dbo.v_LocalizedCIProperties lp ON ci.CI_ID = lp.CI_ID AND lp.LocaleID = 1033
WHERE ci.CI_ID = {deploymentTypeCiId}";

                DataTable dtDetailsResult = databaseContext.QueryService.ExecuteTable(dtDetailsQuery);
                
                if (dtDetailsResult.Rows.Count > 0)
                {
                    Logger.SuccessNested($"Display Name: {dtDetailsResult.Rows[0]["DisplayName"]}");
                    Logger.SuccessNested($"Enabled: {dtDetailsResult.Rows[0]["IsEnabled"]}");
                    Logger.SuccessNested($"Expired: {dtDetailsResult.Rows[0]["IsExpired"]}");
                }

                // Step 3: Find parent Application CI
                Logger.NewLine();

                string parentQuery = $@"
SELECT *
FROM [{db}].dbo.CI_ConfigurationItemRelations
WHERE ToCI_ID = {deploymentTypeCiId}";

                DataTable parentResult = databaseContext.QueryService.ExecuteTable(parentQuery);

                if (parentResult.Rows.Count == 0)
                {
                    Logger.Warning("No parent application found");
                    continue;
                }

                int applicationCiId = Convert.ToInt32(parentResult.Rows[0]["FromCI_ID"]);
                int extFlags = Convert.ToInt32(parentResult.Rows[0]["ExtFlag"]);
                // rowversion: 0x000000002271BCAC

                Logger.Success($"Parent Application CI_ID: {applicationCiId}");
                Logger.SuccessNested($"Relationship ExtFlag: {extFlags}");
                
                // Step 04: Assignments for this Application
                string assignmentsQuery = $@"
SELECT 
    a.AssignmentID,
    a.AssignmentName,
    a.CollectionID,
    c.Name AS CollectionName,
    c.MemberCount,
    a.AssignmentType,
    CASE a.AssignmentType
        WHEN 1 THEN 'Application'
        WHEN 2 THEN 'Configuration Item'
        WHEN 5 THEN 'Software Update'
        WHEN 6 THEN 'Baseline'
        ELSE 'Other (' + CAST(a.AssignmentType AS VARCHAR) + ')'
    END AS AssignmentTypeDescription,
    CASE a.DesiredConfigType
        WHEN 1 THEN 'Install'
        WHEN 2 THEN 'Uninstall'
        ELSE CAST(a.DesiredConfigType AS VARCHAR)
    END AS Intent,
    a.EnforcementDeadline,
    a.StartTime,
    a.ExpirationTime,
    a.CreationTime,
    a.LastModificationTime,
    a.LastModifiedBy,
    CASE a.NotifyUser
        WHEN 0 THEN 'Hide all notifications'
        WHEN 1 THEN 'Display in Software Center only'
        WHEN 2 THEN 'Display in Software Center and show notifications'
        ELSE CAST(a.NotifyUser AS VARCHAR)
    END AS UserNotification,
    CASE a.OverrideServiceWindows
        WHEN 0 THEN 'No'
        WHEN 1 THEN 'Yes'
        ELSE CAST(a.OverrideServiceWindows AS VARCHAR)
    END AS OverrideMaintenanceWindow,
    CASE a.RebootOutsideOfServiceWindows
        WHEN 0 THEN 'No'
        WHEN 1 THEN 'Yes'
        ELSE CAST(a.RebootOutsideOfServiceWindows AS VARCHAR)
    END AS RebootOutsideMaintenanceWindow,
    CASE a.UseGMTTimes
        WHEN 0 THEN 'Client local time'
        WHEN 1 THEN 'UTC'
        ELSE CAST(a.UseGMTTimes AS VARCHAR)
    END AS TimeZone,
    a.SuppressReboot,
    a.AssignmentAction,
    a.AssignmentEnabled,
    a.SourceSite
FROM [{db}].dbo.v_CIAssignment a
LEFT JOIN [{db}].dbo.v_Collection c ON a.CollectionID = c.CollectionID
WHERE a.AssignmentID IN (
    SELECT atc.AssignmentID
    FROM CM_PSC.dbo.v_CIAssignmentToCI AS atc
    WHERE atc.CI_ID = {applicationCiId}
);";

                DataTable assignmentsResult = databaseContext.QueryService.ExecuteTable(assignmentsQuery);
                
                if (assignmentsResult.Rows.Count == 0)
                {
                    Logger.Warning("No assignments found for this application");
                    continue;
                }

                Logger.Success($"Found {assignmentsResult.Rows.Count} assignment(s) for Application CI_ID {applicationCiId}");

                Console.WriteLine(OutputFormatter.ConvertDataTable(assignmentsResult));

                Logger.Info("Next Steps");
                Logger.InfoNested("Use 'cm-assignment <AssignmentID>' to view detailed deployment settings");
                Logger.InfoNested("Use 'cm-collection <CollectionID>' to see which devices are targeted");

                Logger.NewLine();

                // Policy Platform (SML-IF / PolicyPlatform)
                // Stored per generated policy document
                // Used by AppDiscovery.log, AppIntentEval.log
                string documentStoreBodyXML = documentResult.Rows[0]["Body"].ToString();

                // SDM (System Definition Model) representation
                // XML namespace: SystemCenterConfigurationManager/2009/AppMgmtDigest
                // One row = one logical CI version
                string sdmPackageDigestXML = dtDetailsResult.Rows[0]["SDMPackageDigest"].ToString();

                Logger.Info("Extracted XML Snippets:");
                Logger.InfoNested("Policy Platform Document Body (for AppDiscovery.log, AppIntentEval.log)");
                Console.WriteLine(documentStoreBodyXML);
                Logger.NewLine();
                Logger.InfoNested("SDM Package Digest (System Definition Model representation)");
                Console.WriteLine(sdmPackageDigestXML);

                break; // Found it, no need to check other databases
            }

            if (!found)
            {
                Logger.Warning($"Deployment type GUID not found: {_guid}");
                Logger.WarningNested("Make sure you're using the correct GUID from the log file");
                Logger.WarningNested("Example: ScopeId_xxx/DeploymentType_xxx or just DeploymentType_xxx");
            }

            return null;
        }
    }
}
