using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.ConfigMgr
{
    /// <summary>
    /// Display detailed information about a specific ConfigMgr deployment/assignment.
    /// Shows deployment settings, targeted collection, schedule, and execution behavior.
    /// Use this to understand why content keeps getting deployed/reinstalled.
    /// </summary>
    internal class CMDeployment : BaseAction
    {
        [ArgumentMetadata(Position = 0, Description = "Assignment ID to retrieve details for (e.g., 16779074)")]
        private string _assignmentId = "";

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            _assignmentId = GetPositionalArgument(positional, 0, "")
                         ?? GetNamedArgument(named, "assignment", null)
                         ?? GetNamedArgument(named, "a", null)
                         ?? "";

            if (string.IsNullOrWhiteSpace(_assignmentId))
            {
                throw new ArgumentException("Assignment ID is required");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Retrieving assignment details for: {_assignmentId}");

            CMService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            var databases = sccmService.GetSccmDatabases();

            if (databases.Count == 0)
            {
                Logger.Warning("No ConfigMgr databases found");
                return null;
            }

            bool assignmentFound = false;

            foreach (string db in databases)
            {
                string siteCode = CMService.GetSiteCode(db);

                Logger.NewLine();
                Logger.Info($"ConfigMgr database: {db} (Site Code: {siteCode})");

                DataTable assignmentResult = null;

                // Try v_CIAssignment first (for applications with numeric AssignmentID)
                if (int.TryParse(_assignmentId, out int numericAssignmentId))
                {
                    string assignmentQuery = $@"
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
WHERE a.AssignmentID = {numericAssignmentId}";

                    assignmentResult = databaseContext.QueryService.ExecuteTable(assignmentQuery);
                }

                // If not found or ID is not numeric, try v_Advertisement (for packages/task sequences with string AdvertisementID)
                if (assignmentResult == null || assignmentResult.Rows.Count == 0)
                {
                    string advQuery = $@"
SELECT 
    adv.AdvertisementID AS AssignmentID,
    adv.AdvertisementName AS AssignmentName,
    adv.CollectionID,
    c.Name AS CollectionName,
    c.MemberCount,
    adv.PackageID,
    p.Name AS SoftwareName,
    CASE 
        WHEN EXISTS(SELECT 1 FROM [{db}].dbo.v_TaskSequencePackage ts WHERE ts.PackageID = adv.PackageID) THEN 'Task Sequence'
        ELSE 'Package'
    END AS AssignmentTypeDescription,
    adv.ProgramName,
    adv.PresentTime AS StartTime,
    adv.ExpirationTime,
    adv.OfferTypeID,
    CASE adv.OfferTypeID
        WHEN 0 THEN 'Required'
        WHEN 2 THEN 'Available'
        ELSE 'Unknown (' + CAST(adv.OfferTypeID AS VARCHAR) + ')'
    END AS Intent,
    adv.RemoteClientFlags,
    CASE 
        WHEN adv.RemoteClientFlags & 0x00000800 = 0x00000800 THEN 'Always'
        WHEN adv.RemoteClientFlags & 0x00002000 = 0x00002000 THEN 'If Failed'
        WHEN adv.RemoteClientFlags & 0x00004000 = 0x00004000 THEN 'If Succeeded'
        WHEN adv.RemoteClientFlags & 0x00001000 = 0x00001000 THEN 'Never'
        ELSE 'If Failed'
    END AS RerunBehavior,
    CASE 
        WHEN adv.RemoteClientFlags & 0x00000040 = 0x00000040 THEN 'Download from remote DP'
        WHEN adv.RemoteClientFlags & 0x00000010 = 0x00000010 THEN 'Download from local DP'
        WHEN adv.RemoteClientFlags & 0x00000080 = 0x00000080 THEN 'Run from remote DP'
        WHEN adv.RemoteClientFlags & 0x00000008 = 0x00000008 THEN 'Run from local DP'
        ELSE 'Unknown'
    END AS ExecutionMode,
    CASE 
        WHEN adv.AdvertFlags & 0x00020000 = 0x00020000 THEN 'Yes'
        ELSE 'No'
    END AS OverrideMaintenanceWindow,
    CASE 
        WHEN adv.AdvertFlags & 0x00000400 = 0x00000400 THEN 'Yes'
        ELSE 'No'
    END AS AllowUsersToRun,
    adv.AdvertFlags
FROM [{db}].dbo.vAdvertisement adv
LEFT JOIN [{db}].dbo.v_Collection c ON adv.CollectionID = c.CollectionID
LEFT JOIN [{db}].dbo.v_Package p ON adv.PackageID = p.PackageID
WHERE adv.AdvertisementID = '{_assignmentId.Replace("'", "''")}'";

                    assignmentResult = databaseContext.QueryService.ExecuteTable(advQuery);
                    
                    if (assignmentResult.Rows.Count == 0)
                    {
                        continue;
                    }
                }

                assignmentFound = true;
                DataRow assignment = assignmentResult.Rows[0];

                // Determine if this is an Assignment (Application) or Advertisement (Package/Program)
                bool isAdvertisement = assignmentResult.Columns.Contains("AdvertFlags");
                string deploymentKind = isAdvertisement ? "Advertisement (Package/Program)" : "Assignment (Application/CI)";

                // Display assignment details
                Logger.NewLine();
                Logger.Info($"Deployment: {assignment["AssignmentName"]} (ID: {_assignmentId})");
                Logger.InfoNested($"Kind: {deploymentKind}");
                Logger.InfoNested($"Type: {assignment["AssignmentTypeDescription"]}");
                Logger.InfoNested($"Intent: {assignment["Intent"]}");

                Logger.NewLine();
                Logger.Info("Targeted Collection");
                Logger.InfoNested($"Collection ID: {assignment["CollectionID"]}");
                Logger.InfoNested($"Collection Name: {assignment["CollectionName"]}");
                Logger.InfoNested($"Member Count: {assignment["MemberCount"]}");
                Logger.InfoNested($"Use 'cm-collection {assignment["CollectionID"]}' to see member devices");

                Logger.NewLine();
                Logger.Info("Assignment Properties");
                Console.WriteLine(OutputFormatter.ConvertDataTable(assignmentResult));

                // Check for rerun behavior if it's an advertisement
                if (isAdvertisement)
                {
                    int offerType = assignment["OfferTypeID"] != DBNull.Value ? Convert.ToInt32(assignment["OfferTypeID"]) : 2;
                    int remoteClientFlags = assignment["RemoteClientFlags"] != DBNull.Value ? Convert.ToInt32(assignment["RemoteClientFlags"]) : 0;
                    int advertFlags = assignment["AdvertFlags"] != DBNull.Value ? Convert.ToInt32(assignment["AdvertFlags"]) : 0;
                    
                    Logger.NewLine();
                    Logger.Info("Deployment Behavior Analysis");
                    
                    // Check OfferTypeID (0=Required, 2=Available)
                    if (offerType == 0)
                    {
                        Logger.WarningNested("This is a REQUIRED deployment - it will automatically install");
                    }
                    else if (offerType == 2)
                    {
                        Logger.InfoNested("This is an AVAILABLE deployment - users must manually install");
                    }
                    else
                    {
                        Logger.InfoNested($"Offer type: {offerType} (unknown)");
                    }
                    
                    // Check RemoteClientFlags for rerun behavior (bit 11=Always, 12=Never, 13=If Failed, 14=If Succeeded)
                    if ((remoteClientFlags & 0x00000800) == 0x00000800)
                    {
                        Logger.WarningNested("RERUN BEHAVIOR: ALWAYS - This will reinstall even if previously successful!");
                        Logger.WarningNested("This is likely why software keeps getting reinstalled");
                    }
                    else if ((remoteClientFlags & 0x00001000) == 0x00001000)
                    {
                        Logger.InfoNested("RERUN BEHAVIOR: NEVER - Will not run if already executed");
                    }
                    else if ((remoteClientFlags & 0x00002000) == 0x00002000)
                    {
                        Logger.InfoNested("RERUN BEHAVIOR: IF FAILED - Only reruns if previous execution failed");
                    }
                    else if ((remoteClientFlags & 0x00004000) == 0x00004000)
                    {
                        Logger.InfoNested("RERUN BEHAVIOR: IF SUCCEEDED - Only reruns if previous execution succeeded");
                    }
                    else
                    {
                        Logger.InfoNested("RERUN BEHAVIOR: Default (if failed)");
                    }
                    
                    // Check AdvertFlags for other behaviors
                    if ((advertFlags & 0x00020000) == 0x00020000)
                    {
                        Logger.InfoNested("Can override maintenance windows");
                    }
                    
                    if ((advertFlags & 0x00000020) == 0x00000020)
                    {
                        Logger.InfoNested("Announcement timing: IMMEDIATE (runs as soon as received)");
                    }
                    
                    if ((advertFlags & 0x02000000) == 0x02000000)
                    {
                        Logger.InfoNested("Hidden from Software Center (NO_DISPLAY flag set)");
                    }
                }

                // Get deployment statistics
                Logger.NewLine();
                Logger.Info("Deployment Statistics");

                string statsQuery;
                if (isAdvertisement)
                {
                    // For Advertisements, match by CollectionID + PackageID + ProgramName (v_DeploymentSummary.AssignmentID is always 0)
                    string packageId = assignment["PackageID"].ToString();
                    string programName = assignment["ProgramName"].ToString();
                    string collectionId = assignment["CollectionID"].ToString();
                    
                    statsQuery = $@"
SELECT 
    ds.CollectionID,
    ds.SoftwareName,
    ds.NumberTotal,
    ds.NumberSuccess,
    ds.NumberInProgress,
    ds.NumberErrors,
    ds.NumberUnknown,
    ds.NumberOther,
    ds.DeploymentTime,
    ds.ModificationTime,
    ds.SummarizationTime
FROM [{db}].dbo.v_DeploymentSummary ds
WHERE ds.CollectionID = '{collectionId.Replace("'", "''")}'  
    AND ds.PackageID = '{packageId.Replace("'", "''")}'  
    AND ds.ProgramName = '{programName.Replace("'", "''")}'
    AND ds.FeatureType = 2";
                }
                else
                {
                    // For Assignments, match by numeric AssignmentID
                    statsQuery = $@"
SELECT 
    ds.CollectionID,
    ds.SoftwareName,
    ds.NumberTotal,
    ds.NumberSuccess,
    ds.NumberInProgress,
    ds.NumberErrors,
    ds.NumberUnknown,
    ds.NumberOther,
    ds.DeploymentTime,
    ds.ModificationTime,
    ds.SummarizationTime
FROM [{db}].dbo.v_DeploymentSummary ds
WHERE ds.AssignmentID = {numericAssignmentId}";
                }

                DataTable statsResult = databaseContext.QueryService.ExecuteTable(statsQuery);
                
                if (statsResult.Rows.Count > 0)
                {
                    Console.WriteLine(OutputFormatter.ConvertDataTable(statsResult));
                }
                else
                {
                    Logger.Info("No statistics available for this assignment");
                }

                // For Application deployments (AssignmentType = 1), get application and deployment type details
                // Configuration Items, Software Updates, and Baselines have different structures
                if (!isAdvertisement && assignment["AssignmentType"] != DBNull.Value && Convert.ToInt32(assignment["AssignmentType"]) == 1)
                {
                    Logger.NewLine();
                    Logger.Info("Application Details");

                    // Get the CI_ID from v_CIAssignment
                    string ciIdQuery = $@"
SELECT 
    AssignmentID,
    LocalCollectionID AS CI_ID,
    AssignmentName
FROM [{db}].dbo.v_CIAssignment
WHERE AssignmentID = '{_assignmentId.Replace("'", "''")}'";

                    DataTable ciIdResult = databaseContext.QueryService.ExecuteTable(ciIdQuery);

                    if (ciIdResult.Rows.Count == 0 || ciIdResult.Rows[0]["CI_ID"] == DBNull.Value)
                    {
                        Logger.Warning("Could not retrieve CI_ID from v_CIAssignment - application details unavailable");
                    }
                    else
                    {
                        int ciId = Convert.ToInt32(ciIdResult.Rows[0]["CI_ID"]);
                        Logger.InfoNested($"Application CI_ID: {ciId}");

                        string appDetailsQuery = $@"
SELECT 
    ci.CI_ID AS ApplicationCI_ID,
    ci.CI_UniqueID AS ApplicationUniqueID,
    ci.ModelId,
    COALESCE(lp.DisplayName, ci.CI_UniqueID) AS ApplicationDisplayName,
    ci.CIType_ID,
    ci.IsEnabled,
    ci.IsExpired,
    ci.DateCreated,
    ci.DateLastModified
FROM [{db}].dbo.CI_ConfigurationItems ci
LEFT JOIN [{db}].dbo.v_LocalizedCIProperties lp ON ci.CI_ID = lp.CI_ID
WHERE ci.CI_ID = {ciId}";

                        DataTable appDetailsResult = databaseContext.QueryService.ExecuteTable(appDetailsQuery);

                        if (appDetailsResult.Rows.Count > 0)
                        {
                            Console.WriteLine(OutputFormatter.ConvertDataTable(appDetailsResult));

                            DataRow appDetails = appDetailsResult.Rows[0];
                            string appUniqueID = appDetails["ApplicationUniqueID"].ToString();

                            // Get deployment types for this application
                            Logger.NewLine();
                            Logger.Info("Deployment Types");

                            string deploymentTypesQuery = $@"
SELECT 
    dt.CI_ID,
    dt.CI_UniqueID,
    COALESCE(lp.DisplayName, dt.CI_UniqueID) AS DeploymentTypeName,
    dt.CIType_ID,
    dt.IsEnabled,
    dt.IsExpired,
    dt.DateCreated
FROM [{db}].dbo.CI_ConfigurationItems dt
LEFT JOIN [{db}].dbo.v_LocalizedCIProperties lp ON dt.CI_ID = lp.CI_ID
WHERE dt.CI_UniqueID LIKE '{appUniqueID.Replace("'", "''")}/%'
    AND dt.CIType_ID = 21
ORDER BY dt.DateCreated DESC";

                            DataTable deploymentTypesResult = databaseContext.QueryService.ExecuteTable(deploymentTypesQuery);

                            if (deploymentTypesResult.Rows.Count > 0)
                            {
                                Console.WriteLine(OutputFormatter.ConvertDataTable(deploymentTypesResult));
                                Logger.Info($"Found {deploymentTypesResult.Rows.Count} deployment type(s)");

                                // Get detection methods for each deployment type
                                Logger.NewLine();
                                Logger.Info("Detection Methods");

                                foreach (DataRow dtRow in deploymentTypesResult.Rows)
                                {
                                    string dtUniqueID = dtRow["CI_UniqueID"].ToString();
                                    string dtName = dtRow["DeploymentTypeName"].ToString();

                                    Logger.NewLine();
                                    Logger.InfoNested($"Deployment Type: {dtName}");
                                    Logger.InfoNested($"UniqueID: {dtUniqueID}");

                                    string detectionQuery = $@"
SELECT 
    CAST(SDMPackageDigest AS NVARCHAR(MAX)) AS DetectionXML
FROM [{db}].dbo.CI_ConfigurationItems
WHERE CI_UniqueID = '{dtUniqueID.Replace("'", "''")}' 
    AND SDMPackageDigest IS NOT NULL";

                                    DataTable detectionResult = databaseContext.QueryService.ExecuteTable(detectionQuery);

                                    if (detectionResult.Rows.Count > 0 && detectionResult.Rows[0]["DetectionXML"] != DBNull.Value)
                                    {
                                        string detectionXml = detectionResult.Rows[0]["DetectionXML"].ToString();
                                        
                                        // Use centralized parser to extract detection method info
                                        var sdmInfo = CMService.ParseSDMPackageDigest(detectionXml, detailed: true);
                                        
                                        if (!string.IsNullOrEmpty(sdmInfo.DetectionMethodSummary))
                                        {
                                            Logger.InfoNested($"Detection Method: {sdmInfo.DetectionMethodSummary}");
                                        }

                                        Logger.InfoNested($"Use 'cm-dt {dtRow["CI_ID"]}' to see full detection method details");
                                    }
                                    else
                                    {
                                        Logger.InfoNested("No detection method XML found");
                                    }
                                }
                            }
                            else
                            {
                                Logger.Warning("No deployment types found for this application");
                            }
                        }
                        else
                        {
                            Logger.Warning("Application record not found in CI_ConfigurationItems");
                        }
                    }
                }

                // Get specific device status for this assignment
                string deviceStatusQuery;
                
                // Check if this is a Configuration Item/Application deployment or Advertisement
                if (!isAdvertisement)
                {
                    // Configuration Item/Application - use v_AssignmentState_Combined
                    deviceStatusQuery = $@"
SELECT TOP 100
    sys.Name0 AS DeviceName,
    sys.ResourceID,
    asd.StateType,
    asd.StateID,
    CASE asd.StateType
        WHEN 1 THEN 'Detection'
        WHEN 2 THEN 'Evaluation'
        WHEN 3 THEN 'Enforcement'
        WHEN 301 THEN 'Compliance'
        ELSE CAST(asd.StateType AS VARCHAR)
    END AS StateTypeName,
    asd.StateTime,
    asd.LastStatusMessageID,
    asd.LastErrorCode
FROM [{db}].dbo.v_AssignmentState_Combined asd
INNER JOIN [{db}].dbo.v_R_System sys ON asd.ResourceID = sys.ResourceID
WHERE asd.AssignmentID = '{_assignmentId.Replace("'", "''")}'
ORDER BY asd.StateTime DESC";
                }
                else
                {
                    // Advertisement (Package/Task Sequence) - use v_ClientAdvertisementStatus
                    deviceStatusQuery = $@"
SELECT TOP 100
    sys.Name0 AS DeviceName,
    sys.ResourceID,
    aas.LastState,
    aas.LastStateName,
    aas.LastStatusTime,
    aas.LastAcceptanceState,
    aas.LastAcceptanceStateName,
    aas.LastAcceptanceStatusTime,
    aas.LastExecutionResult,
    aas.LastStatusMessageIDName,
    aas.LastAcceptanceMessageIDName
FROM [{db}].dbo.v_ClientAdvertisementStatus aas
INNER JOIN [{db}].dbo.v_R_System sys ON aas.ResourceID = sys.ResourceID
WHERE aas.AdvertisementID = '{_assignmentId.Replace("'", "''")}'
ORDER BY aas.LastStatusTime DESC";
                }

                DataTable deviceStatusResult = databaseContext.QueryService.ExecuteTable(deviceStatusQuery);
                
                if (deviceStatusResult.Rows.Count > 0)
                {
                    Logger.NewLine();
                    Logger.Info("Device Status (showing recent activity)");
                    Console.WriteLine(OutputFormatter.ConvertDataTable(deviceStatusResult));
                    Logger.Info($"Showing {deviceStatusResult.Rows.Count} device status records (most recent first)");
                }
                else
                {
                    Logger.NewLine();
                    Logger.Warning("No device status information available");
                }

                break; // Found assignment, no need to check other databases
            }

            if (!assignmentFound)
            {
                Logger.Warning($"Assignment with ID '{_assignmentId}' not found in any ConfigMgr database");
            }

            return null;
        }
    }
}
