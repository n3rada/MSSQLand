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

                // If not found or ID is not numeric, try vSMS_Advertisement (for packages/task sequences with string OfferID)
                if (assignmentResult == null || assignmentResult.Rows.Count == 0)
                {
                    string advQuery = $@"
SELECT 
    adv.OfferID AS AssignmentID,
    adv.OfferName AS AssignmentName,
    adv.CollectionID,
    c.Name AS CollectionName,
    c.MemberCount,
    adv.PkgID AS PackageID,
    p.Name AS SoftwareName,
    CASE 
        WHEN EXISTS(SELECT 1 FROM [{db}].dbo.v_TaskSequencePackage ts WHERE ts.PackageID = adv.PkgID) THEN 'Task Sequence'
        ELSE 'Package'
    END AS AssignmentTypeDescription,
    adv.PkgProgram AS ProgramName,
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
        WHEN adv.OfferFlags & 0x00020000 = 0x00020000 THEN 'Yes'
        ELSE 'No'
    END AS OverrideMaintenanceWindow,
    CASE 
        WHEN adv.OfferFlags & 0x00000400 = 0x00000400 THEN 'Yes'
        ELSE 'No'
    END AS AllowUsersToRun,
    adv.OfferFlags AS AdvertFlags
FROM [{db}].dbo.vSMS_Advertisement adv
LEFT JOIN [{db}].dbo.v_Collection c ON adv.CollectionID = c.CollectionID
LEFT JOIN [{db}].dbo.v_Package p ON adv.PkgID = p.PackageID
WHERE adv.OfferID = '{_assignmentId.Replace("'", "''")}'";

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

                    // Decode RemoteClientFlags for full details
                    Logger.InfoNested($"RemoteClientFlags: {CMService.DecodeRemoteClientFlags(remoteClientFlags)}");
                    
                    // Decode AdvertFlags for announcement timing and behavior
                    Logger.InfoNested($"AdvertFlags: {CMService.DecodeAdvertFlags(advertFlags)}");
                    
                    // Highlight critical AdvertFlags
                    if ((advertFlags & 0x00020000) == 0x00020000)
                    {
                        Logger.WarningNested("[!] Can override maintenance windows");
                    }
                    
                    if ((advertFlags & 0x00000020) == 0x00000020)
                    {
                        Logger.WarningNested("[!] Announcement timing: IMMEDIATE (runs as soon as received)");
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
                    // For Advertisements, match by CollectionID + PkgID + PkgProgram (v_DeploymentSummary.AssignmentID is always 0)
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

                // For CI-based deployments (Applications, Configuration Items, Baselines, Software Updates),
                // show configuration item identifiers
                if (!isAdvertisement && assignment["AssignmentType"] != DBNull.Value)
                {
                    int assignmentType = Convert.ToInt32(assignment["AssignmentType"]);
                    int assignmentId = Convert.ToInt32(assignment["AssignmentID"]);
                    
                    Logger.NewLine();

                    // Get the CI_ID and basic info using v_CIAssignmentToCI
                    string ciQuery = $@"
SELECT 
    atc.AssignmentID,
    atc.CI_ID,
    ci.CI_UniqueID,
    COALESCE(lp.DisplayName, lcp.Title, ci.CI_UniqueID) AS DisplayName,
    ci.CIType_ID,
    CASE ci.CIType_ID
        WHEN 9 THEN 'Baseline'
        WHEN 10 THEN 'Application'
        WHEN 21 THEN 'Deployment Type'
        WHEN 1 THEN 'Software Update'
        ELSE 'Other (' + CAST(ci.CIType_ID AS VARCHAR) + ')'
    END AS CIType,
    ci.IsEnabled,
    ci.IsExpired
FROM [{db}].dbo.v_CIAssignmentToCI atc
INNER JOIN [{db}].dbo.CI_ConfigurationItems ci ON atc.CI_ID = ci.CI_ID
LEFT JOIN [{db}].dbo.v_LocalizedCIProperties lp ON ci.CI_ID = lp.CI_ID AND lp.LocaleID = 1033
LEFT JOIN [{db}].dbo.CI_LocalizedCIClientProperties lcp ON ci.CI_ID = lcp.CI_ID AND lcp.LocaleID = 1033
WHERE atc.AssignmentID = {assignmentId}";

                    DataTable ciResult = databaseContext.QueryService.ExecuteTable(ciQuery);

                    if (ciResult.Rows.Count == 0)
                    {
                        Logger.Warning("Could not retrieve CI information");
                    }
                    else
                    {
                        Logger.Info("Configuration Item");
                        Console.WriteLine(OutputFormatter.ConvertDataTable(ciResult));
                        
                        DataRow ciRow = ciResult.Rows[0];
                        int ciId = Convert.ToInt32(ciRow["CI_ID"]);
                        int ciTypeId = Convert.ToInt32(ciRow["CIType_ID"]);
                        string ciUniqueID = ciRow["CI_UniqueID"].ToString();

                        // For Applications (CIType_ID = 10), show deployment types
                        if (assignmentType == 1 && ciTypeId == 10)
                        {
                            Logger.NewLine();
                            Logger.Info("Deployment Types");

                            string deploymentTypesQuery = $@"
SELECT 
    dt.CI_ID,
    dt.CI_UniqueID,
    COALESCE(lp.DisplayName, lcp.Title, dt.CI_UniqueID) AS DeploymentTypeName,
    dt.IsEnabled,
    dt.IsExpired
FROM [{db}].dbo.CI_ConfigurationItems dt
LEFT JOIN [{db}].dbo.v_LocalizedCIProperties lp ON dt.CI_ID = lp.CI_ID AND lp.LocaleID = 1033
LEFT JOIN [{db}].dbo.CI_LocalizedCIClientProperties lcp ON dt.CI_ID = lcp.CI_ID AND lcp.LocaleID = 1033
WHERE dt.CI_UniqueID LIKE '{ciUniqueID.Replace("'", "''")}/%'
    AND dt.CIType_ID = 21
ORDER BY dt.DateCreated DESC";

                            DataTable deploymentTypesResult = databaseContext.QueryService.ExecuteTable(deploymentTypesQuery);

                            if (deploymentTypesResult.Rows.Count > 0)
                            {
                                Console.WriteLine(OutputFormatter.ConvertDataTable(deploymentTypesResult));
                                Logger.Success($"Found {deploymentTypesResult.Rows.Count} deployment type(s)");
                                Logger.NewLine();
                                Logger.Info("Use 'cm-dt [CI_ID]' to view deployment type details:");
                                Logger.InfoNested("- Detection methods and verification scripts");
                                Logger.InfoNested("- Install/uninstall commands and execution context");
                                Logger.InfoNested("- Requirements and dependencies");
                                Logger.InfoNested("- Content location and file details");
                            }
                            else
                            {
                                Logger.Warning("No deployment types found");
                            }
                        }
                        // For other CI types, just show pointer to cm-dt
                        else
                        {
                            Logger.NewLine();
                            Logger.Info($"Use 'cm-dt {ciId}' to view detailed {ciRow["CIType"]} information");
                        }
                    }
                }

                // Get specific device status for this assignment
                string deviceStatusQuery;
                
                // Check if this is a Configuration Item/Application deployment or Advertisement
                if (!isAdvertisement)
                {
                    // Configuration Item/Application - use v_AssignmentState_Combined
                    int assignmentId = Convert.ToInt32(assignment["AssignmentID"]);
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
WHERE asd.AssignmentID = {assignmentId}
ORDER BY asd.StateTime DESC";
                }
                else
                {
                    // Advertisement (Package/Task Sequence) - use v_ClientAdvertisementStatus
                    deviceStatusQuery = $@"
SELECT TOP 50
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
                    Logger.Info("Device Status (showing TOP 50 recent activity)");
                    Console.WriteLine(OutputFormatter.ConvertDataTable(deviceStatusResult));
                    Logger.Info($"Showing {deviceStatusResult.Rows.Count} device status records");
                }
                else
                {
                    Logger.NewLine();
                    Logger.Warning("No device status information available");
                }

                // For Advertisements, check content distribution status
                if (isAdvertisement && assignment["PackageID"] != DBNull.Value)
                {
                    string packageId = assignment["PackageID"].ToString();
                    
                    Logger.NewLine();
                    Logger.Info("Content Distribution Status");
                    Logger.InfoNested($"Checking where package {packageId} is distributed");

                    string dpStatusQuery = $@"
SELECT 
    dp.ServerName AS DistributionPoint,
    ds.LastStatusTime,
    dsi.MessageState,
    CASE dsi.MessageState
        WHEN 1 THEN 'Success'
        WHEN 2 THEN 'InProgress'
        WHEN 3 THEN 'Error'
        WHEN 4 THEN 'Retry'
        ELSE CAST(dsi.MessageState AS VARCHAR)
    END AS DistributionState,
    ds.LastStatusMsgID,
    dsi.MessageSeverity,
    ds.MessageType
FROM [{db}].dbo.DistributionPoints dp
INNER JOIN [{db}].dbo.DistributionStatus ds ON dp.NALPath = ds.DPNALPath
LEFT JOIN [{db}].dbo.DistributionStatusInfo dsi ON ds.LastStatusMsgID = dsi.MessageID
WHERE ds.PkgID = '{packageId.Replace("'", "''")}'
ORDER BY ds.LastStatusTime DESC;";

                    DataTable dpStatusResult = databaseContext.QueryService.ExecuteTable(dpStatusQuery);
                    
                    if (dpStatusResult.Rows.Count > 0)
                    {
                        Console.WriteLine(OutputFormatter.ConvertDataTable(dpStatusResult));
                        
                        int successCount = 0;
                        int errorCount = 0;
                        int inProgressCount = 0;
                        
                        foreach (DataRow dpRow in dpStatusResult.Rows)
                        {
                            string state = dpRow["DistributionState"].ToString();
                            if (state == "Success") successCount++;
                            else if (state == "Error") errorCount++;
                            else if (state == "InProgress") inProgressCount++;
                        }
                        
                        Logger.Info($"Content distributed to {dpStatusResult.Rows.Count} DP(s): {successCount} successful, {errorCount} errors, {inProgressCount} in progress");
                        
                        if (successCount == 0)
                        {
                            Logger.Warning("Content not successfully distributed to any DP - this explains 'Waiting for content' status!");
                        }
                        else if (errorCount > 0)
                        {
                            Logger.Warning($"{errorCount} DP(s) have distribution errors - may cause content availability issues");
                        }
                    }
                    else
                    {
                        Logger.Warning($"Package {packageId} is NOT distributed to any distribution points!");
                        Logger.WarningNested("This explains why devices are 'Waiting for content'");
                        Logger.InfoNested($"Action: Distribute package {packageId} to appropriate DPs using ConfigMgr console");
                    }

                    // Show package source path
                    string pkgInfoQuery = $@"
SELECT 
    PackageID,
    Name,
    Description,
    Manufacturer,
    Version,
    PkgSourcePath,
    PkgSourceFlag,
    SourceVersion,
    SourceDate,
    PackageType,
    LastRefreshTime,
    SourceSite
FROM [{db}].dbo.v_Package
WHERE PackageID = '{packageId.Replace("'", "''")}';";

                    DataTable pkgInfoResult = databaseContext.QueryService.ExecuteTable(pkgInfoQuery);
                    
                    if (pkgInfoResult.Rows.Count > 0)
                    {
                        // Add decoded PackageType column
                        DataColumn decodedTypeColumn = pkgInfoResult.Columns.Add("PackageTypeDescription", typeof(string));
                        int packageTypeIndex = pkgInfoResult.Columns["PackageType"].Ordinal;
                        decodedTypeColumn.SetOrdinal(packageTypeIndex);

                        foreach (DataRow row in pkgInfoResult.Rows)
                        {
                            row["PackageTypeDescription"] = CMService.DecodePackageType(row["PackageType"]);
                        }

                        // Remove raw numeric column
                        pkgInfoResult.Columns.Remove("PackageType");

                        Logger.NewLine();
                        Logger.Info("Package Source Information");
                        Console.WriteLine(OutputFormatter.ConvertDataTable(pkgInfoResult));
                        
                        DataRow pkgRow = pkgInfoResult.Rows[0];
                        if (pkgRow["PkgSourcePath"] == DBNull.Value || string.IsNullOrEmpty(pkgRow["PkgSourcePath"].ToString()))
                        {
                            Logger.Warning("Package has no source path defined - cannot distribute to DPs");
                        }
                    }
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
