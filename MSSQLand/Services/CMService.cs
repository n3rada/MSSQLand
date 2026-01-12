// MSSQLand/Services/CMService.cs

using MSSQLand.Models;
using MSSQLand.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;

namespace MSSQLand.Services
{
    /// <summary>
    /// Service for ConfigMgr database detection and common operations.
    /// </summary>
    public class CMService
    {
        private readonly QueryService _queryService;
        private readonly Server _server;
        
        /// <summary>
        /// Cache for vSMS_* views detection per execution server.
        /// </summary>
        private readonly ConcurrentDictionary<string, bool> _hasSccmViewsCache = new();

        public CMService(QueryService queryService, Server server)
        {
            _queryService = queryService;
            _server = server;
        }

        /// <summary>
        /// Gets all ConfigMgr databases on the server.
        /// If the current execution database is an ConfigMgr database, returns only that one.
        /// </summary>
        public List<string> GetSccmDatabases()
        {
            var databases = new List<string>();

            // Check if current execution database is an ConfigMgr database
            string currentDb = _queryService.ExecutionServer.Database;
            if (!string.IsNullOrEmpty(currentDb) && currentDb.StartsWith("CM_", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Debug($"Current execution database '{currentDb}' is an ConfigMgr database");
                databases.Add(currentDb);
                return databases;
            }

            // Query for all ConfigMgr databases
            try
            {
                var result = _queryService.ExecuteTable("SELECT name FROM sys.databases WHERE name LIKE 'CM_%';");
                foreach (DataRow row in result.Rows)
                {
                    databases.Add(row["name"].ToString());
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Failed to enumerate ConfigMgr databases: {ex.Message}");
            }

            return databases;
        }



        /// <summary>
        /// Extracts the site code from an ConfigMgr database name.
        /// </summary>
        public static string GetSiteCode(string databaseName)
        {
            if (string.IsNullOrEmpty(databaseName) || !databaseName.StartsWith("CM_", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return databaseName.Substring(3);
        }

        /// <summary>
        /// Decodes OfferType value into human-readable string.
        /// See: https://learn.microsoft.com/en-us/mem/configmgr/develop/reference/core/servers/configure/sms_advertisement-server-wmi-class#offertype
        /// </summary>
        public static string DecodeOfferType(object offerTypeObj)
        {
            if (offerTypeObj == DBNull.Value)
                return "Unknown";

            int offerType = Convert.ToInt32(offerTypeObj);

            return offerType switch
            {
                0 => "Required",
                2 => "Available",
                _ => $"Unknown ({offerType})"
            };
        }

        /// <summary>
        /// Decodes RemoteClientFlags bitmask into human-readable comma-separated string.
        /// Default value is 48 (0x30 = DOWNLOAD_FROM_LOCAL_DISPPOINT | DONT_RUN_NO_LOCAL_DISPPOINT).
        /// See: https://learn.microsoft.com/en-us/mem/configmgr/develop/reference/core/servers/configure/sms_advertisement-server-wmi-class#remoteclientflags
        /// </summary>
        public static string DecodeRemoteClientFlags(object remoteClientFlagsObj)
        {
            if (remoteClientFlagsObj == DBNull.Value)
                return "None";

            int remoteClientFlags = Convert.ToInt32(remoteClientFlagsObj);
            var flags = new List<string>();

            // Rerun behavior (bits 11-14, mutually exclusive)
            if ((remoteClientFlags & 0x00000800) == 0x00000800) 
                flags.Add("RERUN_ALWAYS");
            else if ((remoteClientFlags & 0x00001000) == 0x00001000) 
                flags.Add("RERUN_NEVER");
            else if ((remoteClientFlags & 0x00002000) == 0x00002000) 
                flags.Add("RERUN_IF_FAILED");
            else if ((remoteClientFlags & 0x00004000) == 0x00004000) 
                flags.Add("RERUN_IF_SUCCEEDED");

            // Distribution point behavior (bits 3-9)
            if ((remoteClientFlags & 0x00000008) == 0x00000008) flags.Add("RUN_FROM_LOCAL_DISPPOINT");
            if ((remoteClientFlags & 0x00000010) == 0x00000010) flags.Add("DOWNLOAD_FROM_LOCAL_DISPPOINT");
            if ((remoteClientFlags & 0x00000020) == 0x00000020) flags.Add("DONT_RUN_NO_LOCAL_DISPPOINT");
            if ((remoteClientFlags & 0x00000040) == 0x00000040) flags.Add("DOWNLOAD_FROM_REMOTE_DISPPOINT");
            if ((remoteClientFlags & 0x00000080) == 0x00000080) flags.Add("RUN_FROM_REMOTE_DISPPOINT");
            if ((remoteClientFlags & 0x00000100) == 0x00000100) flags.Add("DOWNLOAD_ON_DEMAND_FROM_LOCAL_DP");
            if ((remoteClientFlags & 0x00000200) == 0x00000200) flags.Add("DOWNLOAD_ON_DEMAND_FROM_REMOTE_DP");
            
            // Legacy/Other flags
            if ((remoteClientFlags & 0x00000001) == 0x00000001) flags.Add("BATTERY_POWER");
            if ((remoteClientFlags & 0x00000002) == 0x00000002) flags.Add("RUN_FROM_CD");
            if ((remoteClientFlags & 0x00000004) == 0x00000004) flags.Add("DOWNLOAD_FROM_CD");
            if ((remoteClientFlags & 0x00000400) == 0x00000400) flags.Add("BALLOON_REMINDERS_REQUIRED");
            if ((remoteClientFlags & 0x00008000) == 0x00008000) flags.Add("PERSIST_ON_WRITE_FILTER_DEVICES");
            if ((remoteClientFlags & 0x00020000) == 0x00020000) flags.Add("DONT_FALLBACK");
            if ((remoteClientFlags & 0x00040000) == 0x00040000) flags.Add("DP_ALLOW_METERED_NETWORK");

            return flags.Count > 0 ? string.Join(", ", flags) : "None";
        }

        /// <summary>
        /// Decodes AdvertFlags bitmask into human-readable comma-separated string.
        /// NOTE: AdvertFlags controls WHEN/HOW to announce, not Required vs Available (use OfferType for that).
        /// See: https://learn.microsoft.com/en-us/mem/configmgr/develop/reference/core/servers/configure/sms_advertisement-server-wmi-class#advertflags
        /// </summary>
        public static string DecodeAdvertFlags(object advertFlagsObj)
        {
            if (advertFlagsObj == DBNull.Value)
                return "None";

            int advertFlags = Convert.ToInt32(advertFlagsObj);
            var flags = new List<string>();

            if ((advertFlags & 0x00000020) == 0x00000020) flags.Add("IMMEDIATE");
            if ((advertFlags & 0x00000100) == 0x00000100) flags.Add("ONSYSTEMSTARTUP");
            if ((advertFlags & 0x00000200) == 0x00000200) flags.Add("ONUSERLOGON");
            if ((advertFlags & 0x00000400) == 0x00000400) flags.Add("ONUSERLOGOFF");
            if ((advertFlags & 0x00001000) == 0x00001000) flags.Add("OPTIONALPREDOWNLOAD");
            if ((advertFlags & 0x00008000) == 0x00008000) flags.Add("WINDOWS_CE");
            if ((advertFlags & 0x00010000) == 0x00010000) flags.Add("ENABLE_PEER_CACHING");
            if ((advertFlags & 0x00020000) == 0x00020000) flags.Add("DONOT_FALLBACK");
            if ((advertFlags & 0x00040000) == 0x00040000) flags.Add("ENABLE_TS_FROM_CD_AND_PXE");
            if ((advertFlags & 0x00080000) == 0x00080000) flags.Add("APTSINTRANETONLY");
            if ((advertFlags & 0x00100000) == 0x00100000) flags.Add("OVERRIDE_SERVICE_WINDOWS");
            if ((advertFlags & 0x00200000) == 0x00200000) flags.Add("REBOOT_OUTSIDE_OF_SERVICE_WINDOWS");
            if ((advertFlags & 0x00400000) == 0x00400000) flags.Add("WAKE_ON_LAN_ENABLED");
            if ((advertFlags & 0x00800000) == 0x00800000) flags.Add("SHOW_PROGRESS");
            if ((advertFlags & 0x02000000) == 0x02000000) flags.Add("NO_DISPLAY");
            if ((advertFlags & 0x04000000) == 0x04000000) flags.Add("ONSLOWNET");
            if ((advertFlags & 0x10000000) == 0x10000000) flags.Add("TARGETTOWINPE");
            if ((advertFlags & 0x20000000) == 0x20000000) flags.Add("HIDDENINWINPE");

            return flags.Count > 0 ? string.Join(", ", flags) : "None";
        }

        /// <summary>
        /// Decodes PackageType numeric value into human-readable string.
        /// See: https://learn.microsoft.com/en-us/mem/configmgr/develop/reference/core/servers/configure/sms_package-server-wmi-class
        /// </summary>
        public static string DecodePackageType(object packageTypeObj)
        {
            if (packageTypeObj == DBNull.Value)
                return "Package";

            int packageType = Convert.ToInt32(packageTypeObj);

            return packageType switch
            {
                0 => "Package",
                3 => "Driver Package",
                4 => "Task Sequence",
                5 => "Software Update",
                6 => "Device Setting",
                7 => "Virtual App",
                8 => "Application",
                257 => "OS Image",
                258 => "Boot Image",
                259 => "OS Installer",
                _ => $"Unknown ({packageType})"
            };
        }

        /// <summary>
        /// Decodes FeatureType numeric value into human-readable deployment type string.
        /// </summary>
        public static string DecodeFeatureType(object featureTypeObj)
        {
            if (featureTypeObj == DBNull.Value)
                return "Unknown";

            int featureType = Convert.ToInt32(featureTypeObj);

            return featureType switch
            {
                1 => "Application",
                2 => "Program",
                3 => "Mobile Program",
                4 => "Script",
                5 => "Software Update",
                6 => "Baseline",
                7 => "Task Sequence",
                8 => "Content Distribution",
                9 => "Distribution Point Group",
                10 => "Distribution Point Health",
                11 => "Configuration Policy",
                _ => $"Unknown ({featureType})"
            };
        }

        /// <summary>
        /// Decodes DeploymentIntent numeric value into human-readable string.
        /// </summary>
        public static string DecodeDeploymentIntent(object deploymentIntentObj)
        {
            if (deploymentIntentObj == DBNull.Value)
                return "Unknown";

            int deploymentIntent = Convert.ToInt32(deploymentIntentObj);

            return deploymentIntent switch
            {
                1 => "Required",
                2 => "Available",
                3 => "Simulate",
                _ => $"Unknown ({deploymentIntent})"
            };
        }

        /// <summary>
        /// Decodes ProgramFlags bitmask into human-readable semicolon-separated string.
        /// See: https://learn.microsoft.com/en-us/intune/configmgr/develop/reference/core/servers/configure/sms_program-server-wmi-class
        /// </summary>
        public static string DecodeProgramFlags(uint flags)
        {
            var flagsList = new List<string>();

            var flagDefinitions = new Dictionary<uint, string>
            {
                { 0x00000001, "AUTHORIZED_DYNAMIC_INSTALL" },
                { 0x00000002, "USECUSTOMPROGRESSMSG" },
                { 0x00000010, "DEFAULT_PROGRAM" },
                { 0x00000020, "DISABLEMOMALERTONRUNNING" },
                { 0x00000040, "MOMALERTONFAIL" },
                { 0x00000080, "RUN_DEPENDANT_ALWAYS" },
                { 0x00000100, "WINDOWS_CE" },
                { 0x00000400, "COUNTDOWN" },
                { 0x00001000, "DISABLED" },
                { 0x00002000, "UNATTENDED" },
                { 0x00004000, "USERCONTEXT" },
                { 0x00008000, "ADMINRIGHTS" },
                { 0x00010000, "EVERYUSER" },
                { 0x00020000, "NOUSERLOGGEDIN" },
                { 0x00040000, "OKTOQUIT" },
                { 0x00080000, "OKTOREBOOT" },
                { 0x00100000, "USEUNCPATH" },
                { 0x00200000, "PERSISTCONNECTION" },
                { 0x00400000, "RUNMINIMIZED" },
                { 0x00800000, "RUNMAXIMIZED" },
                { 0x01000000, "HIDEWINDOW" },
                { 0x02000000, "OKTOLOGOFF" },
                { 0x04000000, "RUNACCOUNT" },
                { 0x08000000, "ANY_PLATFORM" },
                { 0x10000000, "STILL_RUNNING" },
                { 0x20000000, "SUPPORT_UNINSTALL" },
                { 0x40000000, "PLATFORM_NOT_SUPPORTED" },
                { 0x80000000, "SHOW_IN_ARP" }
            };

            foreach (var kvp in flagDefinitions)
            {
                if ((flags & kvp.Key) == kvp.Key)
                {
                    flagsList.Add(kvp.Value);
                }
            }

            return flagsList.Count > 0 ? string.Join("; ", flagsList) : "None";
        }

        /// <summary>
        /// Checks if the ConfigMgr database has vSMS_* views or uses base tables.
        /// Result is cached per execution server.
        /// </summary>
        public bool HasSccmViews()
        {
            // Check cache first
            if (_hasSccmViewsCache.TryGetValue(_queryService.ExecutionServer.Hostname, out bool hasViews))
            {
                return hasViews;
            }

            try
            {
                var result = _queryService.ExecuteScalar(@"
                    SELECT COUNT(*) 
                    FROM sys.views 
                    WHERE name LIKE 'vSMS_%'");
        
                int viewCount = Convert.ToInt32(result);
                bool viewsExist = viewCount > 0;
        
                Logger.Debug($"Found {viewCount} vSMS_* views - Using {(viewsExist ? "views" : "base tables")}");
                
                // Cache result for this execution server
                _hasSccmViewsCache[_queryService.ExecutionServer.Hostname] = viewsExist;
                
                return viewsExist;
            }
            catch
            {
                Logger.Debug("Failed to check for vSMS_* views, falling back to base tables");
                
                // Cache the fallback result
                _hasSccmViewsCache[_queryService.ExecutionServer.Hostname] = false;
                
                return false;
            }
        }

        /// <summary>
        /// Gets component status information, automatically using views or base tables depending on ConfigMgr version.
        /// </summary>
        public DataTable GetComponentStatus(string database)
        {
            // Try views first (newer ConfigMgr versions)
            if (HasSccmViews())
            {
                try
                {
                    Logger.Debug("Querying component status using vSMS_SC_Component_Status view");
                    return _queryService.ExecuteTable($@"
SELECT *
FROM [{database}].dbo.vSMS_SC_Component_Status
ORDER BY ComponentName;
");
                }
                catch (Exception ex)
                {
                    Logger.Debug($"View query failed, falling back to base tables: {ex.Message}");
                }
            }

            // Fallback to base tables (ConfigMgr 2016 and older)
            Logger.Debug("Querying component status using SC_Component base table");
            return _queryService.ExecuteTable($@"
SELECT 
    ComponentName,
    Name AS MachineName,
    CASE Flags 
        WHEN 2 THEN 'OK'
        WHEN 5 THEN 'Warning'
        WHEN 6 THEN 'Error'
        ELSE 'Unknown'
    END AS Status,
    SiteNumber
FROM [{database}].dbo.SC_Component
ORDER BY ComponentName;
");
        }

        /// <summary>
        /// Gets site system roles, automatically using views or base tables depending on ConfigMgr version.
        /// </summary>
        public DataTable GetSiteSystemRoles(string database)
        {
            // Try views first (newer ConfigMgr versions)
            if (HasSccmViews())
            {
                try
                {
                    Logger.Debug("Querying site system roles using vSMS_SC_SiteSystemRole view");
                    return _queryService.ExecuteTable($@"
SELECT *
FROM [{database}].dbo.vSMS_SC_SiteSystemRole
ORDER BY ServerName, RoleName;
");
                }
                catch (Exception ex)
                {
                    Logger.Debug($"View query failed, falling back to base tables: {ex.Message}");
                }
            }

            // Fallback to base tables (ConfigMgr 2016 and older)
            Logger.Debug("Querying site system roles using SC_SysResUse base table");
            return _queryService.ExecuteTable($@"
SELECT 
    sr.NALPath,
    sr.RoleTypeID,
    CASE sr.RoleTypeID
        WHEN 2 THEN 'SMS Provider'
        WHEN 3 THEN 'Distribution Point'
        WHEN 4 THEN 'Management Point'
        WHEN 5 THEN 'Fallback Status Point'
        WHEN 6 THEN 'Site Server'
        WHEN 11 THEN 'Software Update Point'
        WHEN 16 THEN 'Application Catalog Web Service Point'
        WHEN 17 THEN 'Application Catalog Website Point'
        WHEN 21 THEN 'Reporting Services Point'
        WHEN 22 THEN 'Enrollment Point'
        WHEN 23 THEN 'Enrollment Proxy Point'
        WHEN 25 THEN 'Asset Intelligence Synchronization Point'
        WHEN 27 THEN 'State Migration Point'
        WHEN 28 THEN 'System Health Validator Point'
        WHEN 31 THEN 'Out Of Band Service Point'
        ELSE CAST(sr.RoleTypeID AS VARCHAR(10))
    END AS RoleName,
    sr.NALResType
FROM [{database}].dbo.SC_SysResUse sr
ORDER BY sr.NALPath, sr.RoleTypeID;
");
        }

        /// <summary>
        /// Gets network boundaries, automatically using views or base tables depending on ConfigMgr version.
        /// </summary>
        public DataTable GetBoundaries(string database)
        {
            // Try views first (newer ConfigMgr versions)
            if (HasSccmViews())
            {
                try
                {
                    Logger.Debug("Querying boundaries using vSMS_Boundary view");
                    return _queryService.ExecuteTable($@"
SELECT *
FROM [{database}].dbo.vSMS_Boundary
ORDER BY BoundaryType, DisplayName;
");
                }
                catch (Exception ex)
                {
                    Logger.Debug($"View query failed, falling back to base tables: {ex.Message}");
                }
            }

            // Fallback to base tables (ConfigMgr 2016 and older)
            Logger.Debug("Querying boundaries using BoundaryEx base table");
            return _queryService.ExecuteTable($@"
SELECT 
    b.Name AS DisplayName,
    CASE b.BoundaryType
        WHEN 0 THEN 'IP Subnet'
        WHEN 1 THEN 'AD Site'
        WHEN 2 THEN 'IPv6 Prefix'
        WHEN 3 THEN 'IP Range'
        ELSE CAST(b.BoundaryType AS VARCHAR(10))
    END AS BoundaryType,
    b.Value,
    bg.Name AS BoundaryGroup,
    b.BoundaryID
FROM [{database}].dbo.BoundaryEx b
LEFT JOIN [{database}].dbo.BoundaryGroupMembers bgm ON b.BoundaryID = bgm.BoundaryID
LEFT JOIN [{database}].dbo.BoundaryGroup bg ON bgm.GroupID = bg.GroupID
ORDER BY b.BoundaryType, b.Name;
");
        }

        /// <summary>
        /// Gets distribution points, automatically using views or base tables depending on ConfigMgr version.
        /// </summary>
        public DataTable GetDistributionPoints(string database)
        {
            // Try views first (newer ConfigMgr versions)
            if (HasSccmViews())
            {
                try
                {
                    Logger.Debug("Querying distribution points using vSMS_DistributionPoint view");
                    return _queryService.ExecuteTable($@"
SELECT *
FROM [{database}].dbo.vSMS_DistributionPoint
ORDER BY ServerName;
");
                }
                catch (Exception ex)
                {
                    Logger.Debug($"View query failed, falling back to base tables: {ex.Message}");
                }
            }

            // Fallback to base tables (ConfigMgr 2016 and older)
            Logger.Debug("Querying distribution points using DistributionPoints base table");
            return _queryService.ExecuteTable($@"
SELECT 
    ServerName,
    SMSSiteCode AS SiteCode,
    NALPath,
    Description,
    CASE WHEN IsPXE = 1 THEN 'Yes' ELSE 'No' END AS PXE_Enabled,
    CASE WHEN IsActive = 1 THEN 'Active' ELSE 'Inactive' END AS Status
FROM [{database}].dbo.DistributionPoints
ORDER BY ServerName;
");
        }

        /// <summary>
        /// Holds parsed information from SDMPackageDigest XML for deployment types.
        /// </summary>
        public class SDMPackageInfo
        {
            // Basic fields (always parsed - used by cm-dts)
            public string Technology { get; set; } = "";
            public string InstallCommand { get; set; } = "";
            public string ContentLocation { get; set; } = "";
            public string DetectionType { get; set; } = "";
            public string ExecutionContext { get; set; } = "";

            // Detailed fields (only parsed when detailed=true - used by cm-dt and cm-deployment)
            public string Hosting { get; set; } = "";
            public string UninstallCommand { get; set; } = "";
            public string UninstallSetting { get; set; } = "";
            public string AllowUninstall { get; set; } = "";
            public string WorkingDirectory { get; set; } = "";
            public string OnFastNetwork { get; set; } = "";
            public string OnSlowNetwork { get; set; } = "";
            public string PeerCache { get; set; } = "";
            public string FileName { get; set; } = "";
            public string FileSize { get; set; } = "";
            public string MaxExecuteTime { get; set; } = "";
            public string RunAs32Bit { get; set; } = "";
            public string PostInstallBehavior { get; set; } = "";
            public string RequiresElevatedRights { get; set; } = "";
            public string RequiresUserInteraction { get; set; } = "";
            public string RequiresReboot { get; set; } = "";
            public string UserInteractionMode { get; set; } = "";

            // Detection method details (only when detailed=true)
            public string DetectionMethodSummary { get; set; } = "";
        }

        /// <summary>
        /// Parse SDMPackageDigest XML and extract deployment type information.
        /// </summary>
        /// <param name="xmlContent">The SDMPackageDigest XML content from CI_ConfigurationItems</param>
        /// <param name="detailed">If true, extracts all fields; if false, only basic fields (faster)</param>
        /// <returns>Parsed SDMPackageInfo object</returns>
        public static SDMPackageInfo ParseSDMPackageDigest(string xmlContent, bool detailed = false)
        {
            var info = new SDMPackageInfo();

            if (string.IsNullOrEmpty(xmlContent))
                return info;

            try
            {
                var doc = new System.Xml.XmlDocument();
                doc.LoadXml(xmlContent);

                var nsmgr = new System.Xml.XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("p1", "http://schemas.microsoft.com/SystemCenterConfigurationManager/2009/AppMgmtDigest");
                nsmgr.AddNamespace("dc", "http://schemas.microsoft.com/SystemsCenterConfigurationManager/2009/07/10/DesiredConfiguration");

                // Basic fields (always extract)
                info.Technology = doc.SelectSingleNode("//p1:Technology", nsmgr)?.InnerText ?? "";
                info.InstallCommand = doc.SelectSingleNode("//p1:InstallCommandLine", nsmgr)?.InnerText ?? "";
                info.ContentLocation = doc.SelectSingleNode("//p1:Location", nsmgr)?.InnerText ?? "";
                info.ExecutionContext = doc.SelectSingleNode("//p1:ExecutionContext", nsmgr)?.InnerText ?? "";

                // Detection type
                var enhancedDetection = doc.SelectSingleNode("//p1:EnhancedDetectionMethod", nsmgr);
                if (enhancedDetection != null)
                {
                    info.DetectionType = "Enhanced";
                }
                else
                {
                    var detectAction = doc.SelectSingleNode("//p1:DetectAction", nsmgr);
                    info.DetectionType = detectAction != null ? "Script" : "";
                }

                // Detailed fields (only if requested)
                if (detailed)
                {
                    info.Hosting = doc.SelectSingleNode("//p1:Hosting", nsmgr)?.InnerText ?? "";
                    info.UninstallCommand = doc.SelectSingleNode("//p1:UninstallCommandLine", nsmgr)?.InnerText ?? "";
                    info.UninstallSetting = doc.SelectSingleNode("//p1:UninstallSetting", nsmgr)?.InnerText ?? "";
                    info.AllowUninstall = doc.SelectSingleNode("//p1:AllowUninstall", nsmgr)?.InnerText ?? "";
                    info.WorkingDirectory = doc.SelectSingleNode("//p1:Arg[@Name='WorkingDirectory']", nsmgr)?.InnerText ?? "";
                    info.OnFastNetwork = doc.SelectSingleNode("//p1:OnFastNetwork", nsmgr)?.InnerText ?? "";
                    info.OnSlowNetwork = doc.SelectSingleNode("//p1:OnSlowNetwork", nsmgr)?.InnerText ?? "";
                    info.PeerCache = doc.SelectSingleNode("//p1:PeerCache", nsmgr)?.InnerText ?? "";

                    var fileNode = doc.SelectSingleNode("//p1:File", nsmgr);
                    if (fileNode != null)
                    {
                        info.FileName = fileNode.Attributes["Name"]?.Value ?? "";
                        info.FileSize = fileNode.Attributes["Size"]?.Value ?? "";
                    }

                    info.MaxExecuteTime = doc.SelectSingleNode("//p1:Arg[@Name='MaxExecuteTime']", nsmgr)?.InnerText ?? "";
                    info.RunAs32Bit = doc.SelectSingleNode("//p1:Arg[@Name='RunAs32Bit']", nsmgr)?.InnerText ?? "";
                    info.PostInstallBehavior = doc.SelectSingleNode("//p1:Arg[@Name='PostInstallBehavior']", nsmgr)?.InnerText ?? "";
                    info.RequiresElevatedRights = doc.SelectSingleNode("//p1:Arg[@Name='RequiresElevatedRights']", nsmgr)?.InnerText ?? "";
                    info.RequiresUserInteraction = doc.SelectSingleNode("//p1:Arg[@Name='RequiresUserInteraction']", nsmgr)?.InnerText ?? "";
                    info.RequiresReboot = doc.SelectSingleNode("//p1:Arg[@Name='RequiresReboot']", nsmgr)?.InnerText ?? "";
                    info.UserInteractionMode = doc.SelectSingleNode("//p1:Arg[@Name='UserInteractionMode']", nsmgr)?.InnerText ?? "";

                    // Generate detection method summary
                    var detectionSummary = new List<string>();
                    
                    if (xmlContent.Contains("<File"))
                    {
                        detectionSummary.Add("File-based detection");
                    }
                    
                    if (xmlContent.Contains("<RegistryKey"))
                    {
                        detectionSummary.Add("Registry-based detection");
                    }
                    
                    if (xmlContent.Contains("<Script"))
                    {
                        detectionSummary.Add("Script-based detection (PowerShell/VBScript)");
                    }
                    
                    if (xmlContent.Contains("ProductCode"))
                    {
                        detectionSummary.Add("MSI Product Code detection");
                    }

                    info.DetectionMethodSummary = detectionSummary.Count > 0 
                        ? string.Join(", ", detectionSummary) 
                        : "Unknown detection method";
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Failed to parse SDMPackageDigest XML: {ex.Message}");
            }

            return info;
        }
    }
}
