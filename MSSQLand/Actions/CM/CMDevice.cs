using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.CM
{
    /// <summary>
    /// Display comprehensive information about a specific SCCM-managed device.
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
                Logger.Warning("No SCCM databases found");
                return null;
            }

            bool deviceFound = false;

            foreach (string db in databases)
            {
                string siteCode = CMService.GetSiteCode(db);

                Logger.NewLine();
                Logger.Info($"SCCM database: {db} (Site Code: {siteCode})");

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
    bgb.AccessMP
FROM [{db}].dbo.v_R_System sys
LEFT JOIN [{db}].dbo.v_GS_OPERATING_SYSTEM os ON sys.ResourceID = os.ResourceID
LEFT JOIN [{db}].dbo.v_GS_COMPUTER_SYSTEM cs ON sys.ResourceID = cs.ResourceID
LEFT JOIN [{db}].dbo.BGB_ResStatus bgb ON sys.ResourceID = bgb.ResourceID
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

                // Get collection memberships
                Logger.NewLine();
                Logger.Info("Collection Memberships:");

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
    ds.AssignmentID,
    ds.SoftwareName,
    ds.CollectionID,
    c.Name AS CollectionName,
    CASE ds.FeatureType
        WHEN 1 THEN 'Application'
        WHEN 2 THEN 'Program'
        WHEN 3 THEN 'Mobile Program'
        WHEN 4 THEN 'Script'
        WHEN 5 THEN 'Software Update'
        WHEN 6 THEN 'Baseline'
        WHEN 7 THEN 'Task Sequence'
        WHEN 8 THEN 'Content Distribution'
        WHEN 9 THEN 'Distribution Point Group'
        WHEN 10 THEN 'Distribution Point Health'
        WHEN 11 THEN 'Configuration Policy'
        ELSE CAST(ds.FeatureType AS VARCHAR)
    END AS DeploymentType,
    CASE ds.DeploymentIntent
        WHEN 1 THEN 'Required'
        WHEN 2 THEN 'Available'
        WHEN 3 THEN 'Simulate'
        ELSE CAST(ds.DeploymentIntent AS VARCHAR)
    END AS Intent,
    ds.DeploymentTime,
    ds.NumberSuccess,
    ds.NumberInProgress,
    ds.NumberErrors
FROM [{db}].dbo.v_FullCollectionMembership cm
INNER JOIN [{db}].dbo.v_DeploymentSummary ds ON cm.CollectionID = ds.CollectionID
INNER JOIN [{db}].dbo.v_Collection c ON ds.CollectionID = c.CollectionID
WHERE cm.ResourceID = {resourceId}
ORDER BY ds.DeploymentTime DESC;";

                DataTable deploymentsResult = databaseContext.QueryService.ExecuteTable(deploymentsQuery);
                
                if (deploymentsResult.Rows.Count > 0)
                {
                    Logger.NewLine();
                    Logger.Info("Deployments Targeting This Device:");
                    Console.WriteLine(OutputFormatter.ConvertDataTable(deploymentsResult));
                    Logger.Success($"Found {deploymentsResult.Rows.Count} deployment(s) targeting this device");
                }

                // Get deployed packages
                string packagesQuery = $@"
SELECT DISTINCT
    p.PackageID,
    p.Name AS PackageName,
    p.Version,
    p.Manufacturer,
    p.PackageType,
    p.PkgSourcePath,
    adv.AdvertisementName,
    adv.AdvertFlags
FROM [{db}].dbo.v_FullCollectionMembership cm
INNER JOIN [{db}].dbo.v_Advertisement adv ON cm.CollectionID = adv.CollectionID
INNER JOIN [{db}].dbo.v_Package p ON adv.PackageID = p.PackageID
WHERE cm.ResourceID = {resourceId}
ORDER BY p.Name;";

                DataTable packagesResult = databaseContext.QueryService.ExecuteTable(packagesQuery);
                
                if (packagesResult.Rows.Count > 0)
                {
                    Logger.NewLine();
                    Logger.Info("Packages Deployed to This Device:");
                    Console.WriteLine(OutputFormatter.ConvertDataTable(packagesResult));
                    Logger.Success($"Found {packagesResult.Rows.Count} package(s)");
                }

                // Get deployed applications
                string applicationsQuery = $@"
SELECT DISTINCT
    ci.CI_ID,
    COALESCE(lp.DisplayName, ci.ModelName) AS ApplicationName,
    ci.ModelName,
    ci.IsEnabled,
    ci.IsExpired,
    ds.DeploymentIntent,
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
                
                if (applicationsResult.Rows.Count > 0)
                {
                    Logger.NewLine();
                    Logger.Info("Applications Deployed to This Device:");
                    Console.WriteLine(OutputFormatter.ConvertDataTable(applicationsResult));
                    Logger.Success($"Found {applicationsResult.Rows.Count} application(s)");
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
                
                if (taskSequencesResult.Rows.Count > 0)
                {
                    Logger.NewLine();
                    Logger.Info("Task Sequences Deployed to This Device:");
                    Console.WriteLine(OutputFormatter.ConvertDataTable(taskSequencesResult));
                    Logger.Success($"Found {taskSequencesResult.Rows.Count} task sequence(s)");
                }

                break; // Found device, no need to check other databases
            }

            if (!deviceFound)
            {
                Logger.Warning($"Device '{_deviceName}' not found in any SCCM database");
            }

            return null;
        }
    }
}
