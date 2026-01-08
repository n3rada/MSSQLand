using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.CM
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

                // Get assignment details from v_CIAssignment (for applications) or v_Advertisement (for packages/TS)
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
WHERE a.AssignmentID = {_assignmentId.Replace("'", "''")}";

                DataTable assignmentResult = databaseContext.QueryService.ExecuteTable(assignmentQuery);

                if (assignmentResult.Rows.Count == 0)
                {
                    // Try looking in advertisements for packages/task sequences
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
    END AS AssignmentType,
    adv.ProgramName,
    adv.PresentTime AS StartTime,
    adv.ExpirationTime,
    CASE 
        WHEN adv.AdvertFlags & 0x00000020 = 0x00000020 THEN 'Required'
        ELSE 'Available'
    END AS Intent,
    CASE 
        WHEN adv.AdvertFlags & 0x00000200 = 0x00000200 THEN 'Yes'
        ELSE 'No'
    END AS RerunBehavior,
    CASE 
        WHEN adv.AdvertFlags & 0x00000001 = 0x00000001 THEN 'Download and execute'
        ELSE 'Run from DP'
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
FROM [{db}].dbo.v_Advertisement adv
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

                // Display assignment details
                Logger.NewLine();
                Logger.Success($"Assignment: {assignment["AssignmentName"]} (ID: {_assignmentId})");
                Logger.Info($"Type: {assignment["AssignmentTypeDescription"]}");
                Logger.Info($"Intent: {assignment["Intent"]}");

                Logger.NewLine();
                Logger.Info("Targeted Collection:");
                Logger.InfoNested($"Collection ID: {assignment["CollectionID"]}");
                Logger.InfoNested($"Collection Name: {assignment["CollectionName"]}");
                Logger.InfoNested($"Member Count: {assignment["MemberCount"]}");
                Logger.InfoNested($"Use 'cm-collection {assignment["CollectionID"]}' to see member devices");

                Logger.NewLine();
                Logger.Info("Assignment Properties:");
                Console.WriteLine(OutputFormatter.ConvertDataTable(assignmentResult));

                // Check for rerun behavior if it's an advertisement
                if (assignmentResult.Columns.Contains("AdvertFlags"))
                {
                    int advertFlags = assignment["AdvertFlags"] != DBNull.Value ? Convert.ToInt32(assignment["AdvertFlags"]) : 0;
                    
                    Logger.NewLine();
                    Logger.Info("Deployment Behavior Analysis:");
                    
                    if ((advertFlags & 0x00000020) == 0x00000020)
                    {
                        Logger.Warning("This is a REQUIRED deployment - it will automatically install");
                    }
                    else
                    {
                        Logger.Info("This is an AVAILABLE deployment - users must manually install");
                    }
                    
                    if ((advertFlags & 0x00000200) == 0x00000200)
                    {
                        Logger.Warning("RERUN BEHAVIOR ENABLED - This will reinstall even if previously successful!");
                        Logger.WarningNested("This is likely why software keeps getting reinstalled");
                    }
                    else
                    {
                        Logger.Info("Rerun disabled - Only runs if not already successful");
                    }
                    
                    if ((advertFlags & 0x00020000) == 0x00020000)
                    {
                        Logger.Info("Can override maintenance windows");
                    }
                    
                    if ((advertFlags & 0x00000400) == 0x00000400)
                    {
                        Logger.Info("Users can run independently from Software Center");
                    }
                }

                // Get deployment statistics
                Logger.NewLine();
                Logger.Info("Deployment Statistics:");

                string statsQuery = $@"
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
WHERE ds.AssignmentID = {_assignmentId.Replace("'", "''")}";

                DataTable statsResult = databaseContext.QueryService.ExecuteTable(statsQuery);
                
                if (statsResult.Rows.Count > 0)
                {
                    Console.WriteLine(OutputFormatter.ConvertDataTable(statsResult));
                }
                else
                {
                    Logger.Info("No statistics available for this assignment");
                }

                // Get specific device status for this assignment
                Logger.NewLine();
                Logger.Info("Device Status (showing recent activity):");

                string deviceStatusQuery = $@"
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

                DataTable deviceStatusResult = databaseContext.QueryService.ExecuteTable(deviceStatusQuery);
                
                if (deviceStatusResult.Rows.Count > 0)
                {
                    Console.WriteLine(OutputFormatter.ConvertDataTable(deviceStatusResult));
                    Logger.Info($"Showing {deviceStatusResult.Rows.Count} device status records (most recent first)");
                }
                else
                {
                    Logger.Info("No device status information available");
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
