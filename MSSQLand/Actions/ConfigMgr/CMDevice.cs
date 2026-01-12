using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.ConfigMgr
{
    /// <summary>
    /// Display comprehensive information about a specific ConfigMgr-managed device.
    /// Shows device details, collection memberships, deployments, and all targeted content.
    /// Use this to understand everything happening on a specific device.
    /// </summary>
    internal class CMDevice : BaseAction
    {
        [ArgumentMetadata(Position = 0, Description = "Device name to retrieve details for")]
        private string _deviceName = "";

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            _deviceName = GetPositionalArgument(positional, 0, "")
                       ?? GetNamedArgument(named, "device", null)
                       ?? GetNamedArgument(named, "d", null)
                       ?? "";

            if (string.IsNullOrWhiteSpace(_deviceName))
            {
                throw new ArgumentException("Device name is required");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Retrieving comprehensive device information for: {_deviceName}");

            CMService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            var databases = sccmService.GetSccmDatabases();

            if (databases.Count == 0)
            {
                Logger.Warning("No ConfigMgr databases found");
                return null;
            }

            bool deviceFound = false;

            foreach (string db in databases)
            {
                string siteCode = CMService.GetSiteCode(db);

                Logger.NewLine();
                Logger.Info($"ConfigMgr database: {db} (Site Code: {siteCode})");

                // Get device details
                string deviceQuery = $@"
SELECT 
    sys.ResourceID,
    sys.Name0 AS DeviceName,
    sys.Resource_Domain_OR_Workgr0 AS Domain,
    sys.User_Name0 AS LastUser,
    bgb.IPAddress,
    sys.Operating_System_Name_and0 AS OperatingSystem,
    os.Version0 AS OSVersion,
    sys.Client0 AS HasClient,
    sys.Client_Version0 AS ClientVersion,
    sys.AD_Site_Name0 AS ADSite,
    sys.Decommissioned0 AS Decommissioned,
    cs.Manufacturer0 AS Manufacturer,
    cs.Model0 AS Model,
    bgb.OnlineStatus,
    bgb.LastOnlineTime,
    bgb.LastOfflineTime,
    bgb.AccessMP,
    ws.LastHWScan,
    chs.LastPolicyRequest,
    chs.LastDDR,
    chs.LastSW AS LastSoftwareScan,
    uss.LastScanTime AS LastUpdateScan,
    uss.LastErrorCode AS UpdateScanErrorCode,
    sys.Creation_Date0 AS CreationDate
FROM [{db}].dbo.v_R_System sys
LEFT JOIN [{db}].dbo.v_GS_OPERATING_SYSTEM os ON sys.ResourceID = os.ResourceID
LEFT JOIN [{db}].dbo.v_GS_COMPUTER_SYSTEM cs ON sys.ResourceID = cs.ResourceID
LEFT JOIN [{db}].dbo.BGB_ResStatus bgb ON sys.ResourceID = bgb.ResourceID
LEFT JOIN [{db}].dbo.v_GS_WORKSTATION_STATUS ws ON sys.ResourceID = ws.ResourceID
LEFT JOIN [{db}].dbo.v_CH_ClientSummary chs ON sys.ResourceID = chs.ResourceID
LEFT JOIN [{db}].dbo.v_UpdateScanStatus uss ON sys.ResourceID = uss.ResourceID
WHERE sys.Name0 = '{_deviceName.Replace("'", "''")}';";

                DataTable deviceResult = databaseContext.QueryService.ExecuteTable(deviceQuery);

                if (deviceResult.Rows.Count == 0)
                {
                    continue;
                }

                deviceFound = true;
                DataRow device = deviceResult.Rows[0];
                int resourceId = Convert.ToInt32(device["ResourceID"]);

                // Display device details
                Logger.NewLine();
                Logger.Info($"Device: {device["DeviceName"]} (ResourceID: {resourceId})");
                
                // Domain: NULL = device not domain-joined (workgroup or standalone)
                string domain = device["Domain"] != DBNull.Value ? device["Domain"].ToString() : "(Not domain-joined)";
                Logger.InfoNested($"Domain: {domain}");
                
                // IPAddress: NULL = device never reported IP or not connected to network
                string ipAddress = device["IPAddress"] != DBNull.Value ? device["IPAddress"].ToString() : "(No IP reported)";
                Logger.InfoNested($"IP Address: {ipAddress}");
                
                // Operating System: NULL = OS inventory not yet collected
                string os = device["OperatingSystem"] != DBNull.Value ? device["OperatingSystem"].ToString() : "(Unknown)";
                string osVersion = device["OSVersion"] != DBNull.Value ? device["OSVersion"].ToString() : "(Unknown version)";
                Logger.InfoNested($"Operating System: {os} ({osVersion})");
                
                // Manufacturer/Model: NULL = hardware inventory not run or failed
                string manufacturer = device["Manufacturer"] != DBNull.Value ? device["Manufacturer"].ToString() : "(Unknown)";
                string model = device["Model"] != DBNull.Value ? device["Model"].ToString() : "(Unknown)";
                Logger.InfoNested($"Manufacturer: {manufacturer} | Model: {model}");
                
                // Client: NULL = client not installed or not reporting
                int hasClient = device["HasClient"] != DBNull.Value ? Convert.ToInt32(device["HasClient"]) : 0;
                string clientVersion = device["ClientVersion"] != DBNull.Value ? device["ClientVersion"].ToString() : "(No client)";
                Logger.InfoNested($"Client Installed: {(hasClient == 1 ? "Yes" : "No")} | Version: {clientVersion}");
                
                // OnlineStatus: NULL = device never connected to management point
                int onlineStatus = device["OnlineStatus"] != DBNull.Value ? Convert.ToInt32(device["OnlineStatus"]) : 0;
                Logger.InfoNested($"Online Status: {(onlineStatus == 1 ? "Online" : "Offline")}");
                
                // LastOnlineTime: NULL = device never reported online status
                if (device["LastOnlineTime"] != DBNull.Value)
                {
                    Logger.InfoNested($"Last Online: {Convert.ToDateTime(device["LastOnlineTime"]):yyyy-MM-dd HH:mm:ss}");
                }
                else
                {
                    Logger.InfoNested($"Last Online: (Never connected)");
                }
                
                // LastUser: NULL = no user ever logged in or user tracking disabled
                string lastUser = device["LastUser"] != DBNull.Value ? device["LastUser"].ToString() : "(No user logged in)";
                Logger.InfoNested($"Last User: {lastUser}");
                
                // ADSite: NULL = device not in Active Directory site or AD discovery disabled
                string adSite = device["ADSite"] != DBNull.Value ? device["ADSite"].ToString() : "(No AD site)";
                Logger.InfoNested($"AD Site: {adSite}");
                
                // AccessMP: NULL = no management point assigned or device never contacted MP
                string accessMP = device["AccessMP"] != DBNull.Value ? device["AccessMP"].ToString() : "(No MP assigned)";
                Logger.InfoNested($"Access MP: {accessMP}");
                
                // Decommissioned: NULL = not decommissioned (default to 0)
                int decommissioned = device["Decommissioned"] != DBNull.Value ? Convert.ToInt32(device["Decommissioned"]) : 0;
                Logger.InfoNested($"Decommissioned: {(decommissioned == 1 ? "Yes" : "No")}");

                // Device record creation date
                if (device["CreationDate"] != DBNull.Value)
                {
                    Logger.InfoNested($"First Discovered: {Convert.ToDateTime(device["CreationDate"]):yyyy-MM-dd HH:mm:ss} UTC");
                }

                // Information Refresh Status
                Logger.NewLine();
                Logger.Info("Information Refresh Status");
                
                // === CLIENT COMMUNICATION ===
                // Last Policy Request: NULL = client never requested policy or not installed
                // Policy requests occur every 60 minutes by default - this is the heartbeat of client communication
                if (device["LastPolicyRequest"] != DBNull.Value)
                {
                    DateTime lastPolicy = Convert.ToDateTime(device["LastPolicyRequest"]);
                    TimeSpan policyAge = DateTime.UtcNow - lastPolicy;
                    Logger.InfoNested($"Policy Request (checks for new deployments and settings): {lastPolicy:yyyy-MM-dd HH:mm:ss} UTC ({policyAge.Days}d {policyAge.Hours}h {policyAge.Minutes}m ago)");
                }
                else
                {
                    Logger.InfoNested($"Policy Request (checks for new deployments and settings): Never requested");
                }
                
                // === DISCOVERY ===
                // Last Heartbeat Discovery (DDR): NULL = heartbeat never sent
                // DDR proves device is alive and updates AD site, IP subnet - runs every 7 days or on schedule change
                if (device["LastDDR"] != DBNull.Value)
                {
                    DateTime lastDDR = Convert.ToDateTime(device["LastDDR"]);
                    TimeSpan ddrAge = DateTime.UtcNow - lastDDR;
                    Logger.InfoNested($"Heartbeat DDR (proves device alive, updates AD site): {lastDDR:yyyy-MM-dd HH:mm:ss} UTC ({ddrAge.Days}d {ddrAge.Hours}h {ddrAge.Minutes}m ago)");
                }
                else
                {
                    Logger.InfoNested($"Heartbeat DDR (proves device alive, updates AD site): Never sent");
                }
                
                // === INVENTORY ===
                // Last Hardware Scan: NULL = hardware inventory never run or client not reporting
                // Hardware inventory collects: manufacturer, model, RAM, CPU, disk, BIOS, etc.
                if (device["LastHWScan"] != DBNull.Value)
                {
                    DateTime lastHWScan = Convert.ToDateTime(device["LastHWScan"]);
                    TimeSpan hwAge = DateTime.UtcNow - lastHWScan;
                    Logger.InfoNested($"Hardware Inventory (collects manufacturer, model, RAM, CPU, disk): {lastHWScan:yyyy-MM-dd HH:mm:ss} UTC ({hwAge.Days}d {hwAge.Hours}h {hwAge.Minutes}m ago)");
                }
                else
                {
                    Logger.InfoNested($"Hardware Inventory (collects manufacturer, model, RAM, CPU, disk): Never run");
                }
                
                // Last Software Scan: NULL = software inventory never run
                // Software inventory collects: installed applications, files, registry keys
                if (device["LastSoftwareScan"] != DBNull.Value)
                {
                    DateTime lastSwScan = Convert.ToDateTime(device["LastSoftwareScan"]);
                    TimeSpan swAge = DateTime.UtcNow - lastSwScan;
                    Logger.InfoNested($"Software Inventory (collects installed applications and files): {lastSwScan:yyyy-MM-dd HH:mm:ss} UTC ({swAge.Days}d {swAge.Hours}h {swAge.Minutes}m ago)");
                }
                else
                {
                    Logger.InfoNested($"Software Inventory (collects installed applications and files): Never run");
                }
                
                // === COMPLIANCE ===
                // Last Update Scan: NULL = software update scan never run or WSUS not configured
                // Update scan checks for missing patches against WSUS/SUP - critical for security compliance
                if (device["LastUpdateScan"] != DBNull.Value)
                {
                    DateTime lastUpdateScan = Convert.ToDateTime(device["LastUpdateScan"]);
                    TimeSpan updateAge = DateTime.UtcNow - lastUpdateScan;
                    
                    // Check for scan errors
                    int errorCode = device["UpdateScanErrorCode"] != DBNull.Value ? Convert.ToInt32(device["UpdateScanErrorCode"]) : 0;
                    string errorInfo = errorCode != 0 ? $" (Error: 0x{errorCode:X8})" : "";
                    
                    Logger.InfoNested($"Update Scan (scans for missing Windows patches): {lastUpdateScan:yyyy-MM-dd HH:mm:ss} UTC ({updateAge.Days}d {updateAge.Hours}h {updateAge.Minutes}m ago){errorInfo}");
                }
                else
                {
                    Logger.InfoNested($"Update Scan (scans for missing Windows patches): Never run or WSUS not configured");
                }


                // Get collection memberships
                Logger.NewLine();
                

                string collectionsQuery = $@"
SELECT 
    c.CollectionID,
    c.Name AS CollectionName,
    c.CollectionType,
    c.MemberCount,
    CASE c.CollectionType
        WHEN 1 THEN 'User'
        WHEN 2 THEN 'Device'
        ELSE 'Other'
    END AS Type
FROM [{db}].dbo.v_FullCollectionMembership cm
INNER JOIN [{db}].dbo.v_Collection c ON cm.CollectionID = c.CollectionID
WHERE cm.ResourceID = {resourceId}
ORDER BY c.CollectionID;";

                DataTable collectionsResult = databaseContext.QueryService.ExecuteTable(collectionsQuery);
                
                if (collectionsResult.Rows.Count > 0)
                {
                    Logger.Info("Collection Memberships");
                    Console.WriteLine(OutputFormatter.ConvertDataTable(collectionsResult));
                    Logger.Success($"Device is member of {collectionsResult.Rows.Count} collection(s)");
                }
                else
                {
                    Logger.Warning("Device is not a member of any collections");
                }

                // Get deployments targeting this device (via collections)
                string deploymentsQuery = $@"
SELECT DISTINCT
    CASE 
        WHEN ds.FeatureType = 2 THEN adv.AdvertisementID
        ELSE CAST(ds.AssignmentID AS VARCHAR)
    END AS DeploymentID,
    ds.SoftwareName,
    ds.CollectionID,
    c.Name AS CollectionName,
    ds.FeatureType AS FeatureTypeRaw,
    ds.DeploymentIntent AS DeploymentIntentRaw,
    ds.DeploymentTime,
    ds.NumberSuccess,
    ds.NumberInProgress,
    ds.NumberErrors
FROM [{db}].dbo.v_FullCollectionMembership cm
INNER JOIN [{db}].dbo.v_DeploymentSummary ds ON cm.CollectionID = ds.CollectionID
INNER JOIN [{db}].dbo.v_Collection c ON ds.CollectionID = c.CollectionID
LEFT JOIN [{db}].dbo.v_Advertisement adv ON ds.CollectionID = adv.CollectionID 
    AND ds.PackageID = adv.PackageID 
    AND ds.ProgramName = adv.ProgramName
    AND ds.FeatureType = 2
WHERE cm.ResourceID = {resourceId}
ORDER BY ds.DeploymentTime DESC;";

                DataTable deploymentsResult = databaseContext.QueryService.ExecuteTable(deploymentsQuery);
                
                Logger.NewLine();
                
                if (deploymentsResult.Rows.Count > 0)
                {
                    Logger.Info("Deployments Targeting This Device");
                    Logger.InfoNested("What's supposed to run here?")
                    
                    // Add decoded columns and remove raw numeric columns
                    DataColumn decodedFeatureColumn = deploymentsResult.Columns.Add("DeploymentType", typeof(string));
                    int featureTypeRawIndex = deploymentsResult.Columns["FeatureTypeRaw"].Ordinal;
                    decodedFeatureColumn.SetOrdinal(featureTypeRawIndex);

                    DataColumn decodedIntentColumn = deploymentsResult.Columns.Add("Intent", typeof(string));
                    int deploymentIntentRawIndex = deploymentsResult.Columns["DeploymentIntentRaw"].Ordinal;
                    decodedIntentColumn.SetOrdinal(deploymentIntentRawIndex);

                    foreach (DataRow row in deploymentsResult.Rows)
                    {
                        row["DeploymentType"] = CMService.DecodeFeatureType(row["FeatureTypeRaw"]);
                        row["Intent"] = CMService.DecodeDeploymentIntent(row["DeploymentIntentRaw"]);
                    }

                    // Remove raw numeric columns
                    deploymentsResult.Columns.Remove("FeatureTypeRaw");
                    deploymentsResult.Columns.Remove("DeploymentIntentRaw");

                    Console.WriteLine(OutputFormatter.ConvertDataTable(deploymentsResult));
                    Logger.Success($"Found {deploymentsResult.Rows.Count} deployment(s) targeting this device");
                }
                else
                {
                    Logger.Warning("No deployments targeting this device");
                }

                // Get deployed packages with status
                string packagesQuery = $@"
SELECT DISTINCT
    p.PackageName,
    cas.LastStateName AS ExecutionState,
    CASE 
        WHEN cas.LastAcceptanceState = 0 THEN 'Waiting'
        WHEN cas.LastAcceptanceState = 1 THEN 'Accepted'
        WHEN cas.LastAcceptanceState = 2 THEN 'Rejected'
        WHEN cas.LastAcceptanceState = 3 THEN 'Expired'
        ELSE CAST(cas.LastAcceptanceState AS VARCHAR)
    END AS AcceptanceStatus,
    cas.LastStatusTime,
    cas.LastExecutionResult,
    adv.AdvertisementID,
    adv.AdvertisementName,
    adv.ProgramName,
    p.PackageID,
    p.Version,
    p.Manufacturer,
    p.PackageType AS PackageTypeRaw,
    adv.OfferType,
    adv.RemoteClientFlags,
    adv.AdvertFlags,
    p.PkgSourcePath
FROM [{db}].dbo.v_FullCollectionMembership cm
INNER JOIN [{db}].dbo.v_Advertisement adv ON cm.CollectionID = adv.CollectionID
INNER JOIN [{db}].dbo.v_Collection c ON adv.CollectionID = c.CollectionID
INNER JOIN [{db}].dbo.v_Package p ON adv.PackageID = p.PackageID
LEFT JOIN [{db}].dbo.v_ClientAdvertisementStatus cas ON cas.AdvertisementID = adv.AdvertisementID AND cas.ResourceID = {resourceId}
WHERE cm.ResourceID = {resourceId}
ORDER BY 
    CASE 
        WHEN cas.LastStateName LIKE '%Fail%' OR cas.LastStateName LIKE '%Error%' THEN 0
        WHEN cas.LastStateName LIKE '%Running%' OR cas.LastStateName = 'Waiting' THEN 1
        WHEN cas.LastStateName = 'Succeeded' THEN 2
        ELSE 3
    END,
    cas.LastStatusTime DESC,
    p.Name;";

                DataTable packagesResult = databaseContext.QueryService.ExecuteTable(packagesQuery);
                
                Logger.NewLine();
                
                
                if (packagesResult.Rows.Count > 0)
                {
                    Logger.Info("Packages Deployed to This Device");
                    Logger.InfoNested("What packages are supposed to be here and their status?")
                    
                    // Add decoded PackageType column and remove raw numeric column
                    DataColumn decodedTypeColumn = packagesResult.Columns.Add("PackageType", typeof(string));
                    int packageTypeRawIndex = packagesResult.Columns["PackageTypeRaw"].Ordinal;
                    decodedTypeColumn.SetOrdinal(packageTypeRawIndex);

                    // Add decoded OfferType column
                    DataColumn decodedOfferColumn = packagesResult.Columns.Add("DeploymentPurpose", typeof(string));
                    int offerTypeIndex = packagesResult.Columns["OfferType"].Ordinal;
                    decodedOfferColumn.SetOrdinal(offerTypeIndex);

                    // Add decoded RemoteClientFlags column
                    DataColumn decodedRemoteColumn = packagesResult.Columns.Add("RerunBehavior", typeof(string));
                    int remoteClientFlagsIndex = packagesResult.Columns["RemoteClientFlags"].Ordinal;
                    decodedRemoteColumn.SetOrdinal(remoteClientFlagsIndex);

                    // Add decoded AdvertFlags column
                    DataColumn decodedAdvertColumn = packagesResult.Columns.Add("AnnouncementFlags", typeof(string));
                    int advertFlagsIndex = packagesResult.Columns["AdvertFlags"].Ordinal;
                    decodedAdvertColumn.SetOrdinal(advertFlagsIndex);

                    foreach (DataRow row in packagesResult.Rows)
                    {
                        row["PackageType"] = CMService.DecodePackageType(row["PackageTypeRaw"]);
                        row["DeploymentPurpose"] = CMService.DecodeOfferType(row["OfferType"]);
                        row["RerunBehavior"] = CMService.DecodeRemoteClientFlags(row["RemoteClientFlags"]);
                        row["AnnouncementFlags"] = CMService.DecodeAdvertFlags(row["AdvertFlags"]);
                    }

                    // Remove raw numeric columns
                    packagesResult.Columns.Remove("PackageTypeRaw");
                    packagesResult.Columns.Remove("OfferType");
                    packagesResult.Columns.Remove("RemoteClientFlags");
                    packagesResult.Columns.Remove("AdvertFlags");

                    Console.WriteLine(OutputFormatter.ConvertDataTable(packagesResult));
                    Logger.Success($"Found {packagesResult.Rows.Count} package(s)");
                }
                else
                {
                    Logger.Warning("No packages deployed to this device");
                }

                // Get deployed applications with CI_UniqueID for tracking
                string applicationsQuery = $@"
SELECT DISTINCT
    ci.CI_ID,
    ci.CI_UniqueID,
    COALESCE(lp.DisplayName, ci.ModelName) AS ApplicationName,
    ci.ModelName,
    ci.IsEnabled,
    ci.IsExpired,
    ds.AssignmentID,
    ds.DeploymentIntent AS DeploymentIntentRaw,
    ds.DeploymentTime
FROM [{db}].dbo.v_FullCollectionMembership cm
INNER JOIN [{db}].dbo.v_DeploymentSummary ds ON cm.CollectionID = ds.CollectionID
INNER JOIN [{db}].dbo.v_ConfigurationItems ci ON ds.SoftwareName = ci.ModelName OR ds.SoftwareName LIKE '%' + ci.ModelName + '%'
LEFT JOIN (
    SELECT CI_ID, MIN(DisplayName) AS DisplayName
    FROM [{db}].dbo.v_LocalizedCIProperties
    WHERE DisplayName IS NOT NULL AND DisplayName != ''
    GROUP BY CI_ID
) lp ON ci.CI_ID = lp.CI_ID
WHERE cm.ResourceID = {resourceId}
    AND ci.CIType_ID = 10
    AND ds.FeatureType = 1
ORDER BY ApplicationName;";

                DataTable applicationsResult = databaseContext.QueryService.ExecuteTable(applicationsQuery);
                
                Logger.NewLine();
                
                if (applicationsResult.Rows.Count > 0)
                {
                    Logger.Info("Applications Deployed to This Device");
                    
                    // Add decoded DeploymentIntent column and remove raw numeric column
                    DataColumn decodedIntentColumn = applicationsResult.Columns.Add("Intent", typeof(string));
                    int deploymentIntentRawIndex = applicationsResult.Columns["DeploymentIntentRaw"].Ordinal;
                    decodedIntentColumn.SetOrdinal(deploymentIntentRawIndex);

                    foreach (DataRow row in applicationsResult.Rows)
                    {
                        row["Intent"] = CMService.DecodeDeploymentIntent(row["DeploymentIntentRaw"]);
                    }

                    // Remove raw numeric column
                    applicationsResult.Columns.Remove("DeploymentIntentRaw");

                    Console.WriteLine(OutputFormatter.ConvertDataTable(applicationsResult));
                    Logger.Success($"Found {applicationsResult.Rows.Count} application(s)");
                }
                else
                {
                    Logger.Warning("No applications deployed to this device");
                }

                // Get task sequences
                string taskSequencesQuery = $@"
SELECT DISTINCT
    ts.PackageID,
    ts.Name AS TaskSequenceName,
    ts.Description,
    adv.AdvertisementName,
    adv.PresentTime,
    adv.ExpirationTime
FROM [{db}].dbo.v_FullCollectionMembership cm
INNER JOIN [{db}].dbo.v_Advertisement adv ON cm.CollectionID = adv.CollectionID
INNER JOIN [{db}].dbo.v_TaskSequencePackage ts ON adv.PackageID = ts.PackageID
WHERE cm.ResourceID = {resourceId}
ORDER BY ts.Name;";

                DataTable taskSequencesResult = databaseContext.QueryService.ExecuteTable(taskSequencesQuery);
                
                Logger.NewLine();
                
                if (taskSequencesResult.Rows.Count > 0)
                {
                    Logger.Info("Task Sequences Deployed to This Device");
                    Console.WriteLine(OutputFormatter.ConvertDataTable(taskSequencesResult));
                    Logger.Success($"Found {taskSequencesResult.Rows.Count} task sequence(s)");
                }
                else
                {
                    Logger.Warning("No task sequences deployed to this device");
                }

                break; // Found device, no need to check other databases
            }

            if (!deviceFound)
            {
                Logger.Warning($"Device '{_deviceName}' not found in any ConfigMgr database");
            }

            return null;
        }
    }
}
