using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;
using System.Linq;

namespace MSSQLand.Actions.ConfigMgr
{
    /// <summary>
    /// Enumerate ConfigMgr-managed devices with filtering by attributes.
    /// Use this for device discovery, inventory queries, and finding devices by location/user/collection.
    /// InventoriedUsers column shows all users from hardware inventory (console, RDP, scheduled tasks, services).
    /// For detailed user statistics (logon counts, time spent), use cm-device-users.
    /// For ConfigMgr client health diagnostics and troubleshooting, use sccm-health instead.
    /// </summary>
    internal class CMDevices : BaseAction
    {
        [ArgumentMetadata(Position = 0, LongName = "device", Description = "Filter by device name")]
        private string _device = "";

        [ArgumentMetadata(Position = 1, ShortName = "d", LongName = "domain", Description = "Filter by domain")]
        private string _domain = "";

        [ArgumentMetadata(Position = 2, ShortName = "u", LongName = "user", Description = "Filter by username")]
        private string _username = "";

        [ArgumentMetadata(Position = 3, ShortName = "i", LongName = "ip", Description = "Filter by IP address")]
        private string _ip = "";

        [ArgumentMetadata(Position = 4, ShortName = "c", LongName = "collection", Description = "Filter by collection name")]
        private string _collection = "";

        [ArgumentMetadata(Position = 5, LongName = "dn", Description = "Filter by distinguished name (e.g., 'Domain Controllers' for DCs)")]
        private string _distinguishedName = "";

        [ArgumentMetadata(Position = 6, ShortName = "o", LongName = "online", Description = "Show only online devices (default: false)")]
        private bool _onlineOnly = false;

        [ArgumentMetadata(Position = 7, LongName = "require-lastuser", Description = "Show only devices with a LastUser value (default: false)")]
        private bool _requireLastUser = false;

        [ArgumentMetadata(Position = 8, LongName = "no-user", Description = "Show only devices without a LastUser value (default: false)")]
        private bool _noUser = false;

        [ArgumentMetadata(Position = 9, LongName = "client-only", Description = "Show only devices with ConfigMgr client installed (default: false)")]
        private bool _clientOnly = false;

        [ArgumentMetadata(Position = 10, LongName = "active", Description = "Show only active (non-decommissioned) devices (default: false)")]
        private bool _activeOnly = false;

        [ArgumentMetadata(Position = 11, LongName = "last-seen-days", Description = "Show devices seen online in last N days")]
        private int _lastSeenDays = 0;

        [ArgumentMetadata(Position = 12,  LongName = "limit", Description = "Limit number of results (default: 50)")]
        private int _limit = 50;

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            _device = GetNamedArgument(named, "device", null)
                   ?? GetPositionalArgument(positional, 0, "");

            _domain = GetNamedArgument(named, "d", null)
                   ?? GetNamedArgument(named, "domain", null)
                   ?? GetPositionalArgument(positional, 1, "");

            _username = GetNamedArgument(named, "u", null)
                     ?? GetNamedArgument(named, "username", null) ?? "";

            _ip = GetNamedArgument(named, "i", null)
               ?? GetNamedArgument(named, "ip", null) ?? "";

            _collection = GetNamedArgument(named, "c", null)
                       ?? GetNamedArgument(named, "collection", null)
                       ?? GetPositionalArgument(positional, 2, "");

            _distinguishedName = GetNamedArgument(named, "dn", null)
                              ?? GetNamedArgument(named, "distinguished-name", null)
                              ?? GetPositionalArgument(positional, 3, "");

            string onlineStr = GetNamedArgument(named, "o", null)
                            ?? GetNamedArgument(named, "online", null);
            if (!string.IsNullOrEmpty(onlineStr))
            {
                _onlineOnly = bool.Parse(onlineStr);
            }

            string requireLastUserStr = GetNamedArgument(named, "require-lastuser", null);
            if (!string.IsNullOrEmpty(requireLastUserStr))
            {
                _requireLastUser = bool.Parse(requireLastUserStr);
            }

            string noUserStr = GetNamedArgument(named, "n", null)
                            ?? GetNamedArgument(named, "no-user", null);
            if (!string.IsNullOrEmpty(noUserStr))
            {
                _noUser = bool.Parse(noUserStr);
            }

            string clientOnlyStr = GetNamedArgument(named, "client-only", null);
            if (!string.IsNullOrEmpty(clientOnlyStr))
            {
                _clientOnly = bool.Parse(clientOnlyStr);
            }

            string activeOnlyStr = GetNamedArgument(named, "a", null)
                                ?? GetNamedArgument(named, "active", null);
            if (!string.IsNullOrEmpty(activeOnlyStr))
            {
                _activeOnly = bool.Parse(activeOnlyStr);
            }

            string lastSeenDaysStr = GetNamedArgument(named, "last-seen-days", null);
            if (!string.IsNullOrEmpty(lastSeenDaysStr))
            {
                _lastSeenDays = int.Parse(lastSeenDaysStr);
            }

            string limitStr = GetNamedArgument(named, "l", null)
                           ?? GetNamedArgument(named, "limit", null)
                           ?? GetPositionalArgument(positional, 4);
            if (!string.IsNullOrEmpty(limitStr))
            {
                _limit = int.Parse(limitStr);
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            string deviceMsg = !string.IsNullOrEmpty(_device) ? $" (device: {_device})" : "";
            string domainMsg = !string.IsNullOrEmpty(_domain) ? $" (domain: {_domain})" : "";
            string usernameMsg = !string.IsNullOrEmpty(_username) ? $" (username: {_username})" : "";
            string ipMsg = !string.IsNullOrEmpty(_ip) ? $" (ip: {_ip})" : "";
            string collectionMsg = !string.IsNullOrEmpty(_collection) ? $" (collection: {_collection})" : "";
            string dnMsg = !string.IsNullOrEmpty(_distinguishedName) ? $" (dn: {_distinguishedName})" : "";
            string onlineMsg = _onlineOnly ? " (online only)" : "";
            string lastUserMsg = _requireLastUser ? " (with last user)" : "";
            string noUserMsg = _noUser ? " (no user)" : "";
            string clientOnlyMsg = _clientOnly ? " (client only)" : "";
            string activeOnlyMsg = _activeOnly ? " (active only)" : "";
            string lastSeenMsg = _lastSeenDays > 0 ? $" (seen in last {_lastSeenDays} days)" : "";
            Logger.TaskNested($"Enumerating ConfigMgr devices{deviceMsg}{domainMsg}{usernameMsg}{ipMsg}{collectionMsg}{dnMsg}{onlineMsg}{lastUserMsg}{noUserMsg}{clientOnlyMsg}{activeOnlyMsg}{lastSeenMsg}");
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
                Logger.NewLine();
                string siteCode = CMService.GetSiteCode(db);
                Logger.Info($"ConfigMgr database: {db} (Site Code: {siteCode})");

                try
                {
                    string whereClause = "WHERE 1=1";
                    
                    // Add device name filter
                    if (!string.IsNullOrEmpty(_device))
                    {
                        whereClause += $" AND sys.Name0 LIKE '%{_device.Replace("'", "''")}%'";
                    }

                    // Add domain filter
                    if (!string.IsNullOrEmpty(_domain))
                    {
                        whereClause += $" AND sys.Resource_Domain_OR_Workgr0 LIKE '%{_domain.Replace("'", "''")}%'";
                    }

                    // Add username filter (searches both LastConsoleUser and InventoriedUsers)
                    if (!string.IsNullOrEmpty(_username))
                    {
                        whereClause += $@" AND (
                            sys.User_Name0 LIKE '%{_username.Replace("'", "''")}%' 
                            OR EXISTS (
                                SELECT 1 
                                FROM [{db}].dbo.v_GS_SYSTEM_CONSOLE_USER cu_filter
                                WHERE cu_filter.ResourceID = sys.ResourceID 
                                AND cu_filter.SystemConsoleUser0 LIKE '%{_username.Replace("'", "''")}%'
                            )
                        )";
                    }

                    // Add IP address filter
                    if (!string.IsNullOrEmpty(_ip))
                    {
                        whereClause += $" AND bgb.IPAddress LIKE '{_ip.Replace("'", "''")}%'";
                    }

                    // Add collection filter
                    if (!string.IsNullOrEmpty(_collection))
                    {
                        whereClause += $@" AND EXISTS (
                            SELECT 1 
                            FROM [{db}].dbo.v_FullCollectionMembership cm_filter
                            INNER JOIN [{db}].dbo.v_Collection col_filter ON cm_filter.CollectionID = col_filter.CollectionID
                            WHERE cm_filter.ResourceID = sys.ResourceID 
                            AND col_filter.Name LIKE '%{_collection.Replace("'", "''")}%'
                        )";
                    }

                    // Add distinguished name filter
                    if (!string.IsNullOrEmpty(_distinguishedName))
                    {
                        whereClause += $" AND sys.Distinguished_Name0 LIKE '%{_distinguishedName.Replace("'", "''")}%'";
                    }

                    // Add online status filter
                    if (_onlineOnly)
                    {
                        whereClause += " AND bgb.OnlineStatus = 1";
                    }

                    // Add last user filter
                    if (_requireLastUser)
                    {
                        whereClause += " AND sys.User_Name0 IS NOT NULL AND sys.User_Name0 != ''";
                    }

                    // Add no user filter
                    if (_noUser)
                    {
                        whereClause += " AND (sys.User_Name0 IS NULL OR sys.User_Name0 = '')";
                    }

                    // Add client-only filter
                    if (_clientOnly)
                    {
                        whereClause += " AND sys.Client0 = 1";
                    }

                    // Add active-only filter (non-decommissioned)
                    if (_activeOnly)
                    {
                        whereClause += " AND sys.Decommissioned0 = 0";
                    }

                    // Add last-seen-days filter
                    if (_lastSeenDays > 0)
                    {
                        whereClause += $" AND bgb.LastOnlineTime >= DATEADD(DAY, -{_lastSeenDays}, GETDATE())";
                    }

                    string topClause = _limit > 0 ? $"TOP {_limit}" : "";

                    string query = $@"
SELECT DISTINCT {topClause}
    sys.Name0 AS DeviceName,
    sys.ResourceID,
    sys.Resource_Domain_OR_Workgr0 AS Domain,
    sys.Primary_Group_ID0 AS PrimaryGroupRID,
    sys.Distinguished_Name0 AS DistinguishedName,
    sys.User_Account_Control0 AS UserAccountControl,
    sys.Operating_System_Name_and0 AS OperatingSystem,
    sys.Client0 AS Client,
    sys.Client_Version0 AS ClientVersion,
    sys.Decommissioned0 AS Decommissioned,
    bgb.OnlineStatus,
    bgb.LastOnlineTime,
    bgb.LastOfflineTime,
    chs.LastPolicyRequest,
    sys.Last_Logon_Timestamp0 AS ComputerLastADAuth,
    sys.AD_Site_Name0 AS ADSite,
    bgb.IPAddress,
    bgb.AccessMP,
    sys.User_Name0 AS LastConsoleUser,
    STUFF((
        SELECT ', ' + cu.SystemConsoleUser0
        FROM [{db}].dbo.v_GS_SYSTEM_CONSOLE_USER cu
        WHERE cu.ResourceID = sys.ResourceID
        ORDER BY cu.LastConsoleUse0 DESC
        FOR XML PATH(''), TYPE
    ).value('.', 'NVARCHAR(MAX)'), 1, 2, '') AS InventoriedUsers,
    sys.Is_Virtual_Machine0 AS IsVirtualMachine,
    sys.Virtual_Machine_Type0 AS VMTypeRaw,
    sys.Virtual_Machine_Host_Name0 AS VMHostName,
    sys.ManagementAuthority AS ManagementAuthority,
    sys.AADDeviceID,
    sys.AADTenantID,
    sys.Creation_Date0 AS RegisteredDate,
    sys.SMS_Unique_Identifier0 AS SMSID,
    STUFF((
        SELECT ', ' + col.Name
        FROM [{db}].dbo.v_FullCollectionMembership cm
        INNER JOIN [{db}].dbo.v_Collection col ON cm.CollectionID = col.CollectionID
        WHERE cm.ResourceID = sys.ResourceID AND col.CollectionType = 2
        FOR XML PATH(''), TYPE
    ).value('.', 'NVARCHAR(MAX)'), 1, 2, '') AS Collections
FROM [{db}].dbo.v_R_System sys
LEFT JOIN [{db}].dbo.BGB_ResStatus bgb ON sys.ResourceID = bgb.ResourceID
LEFT JOIN [{db}].dbo.v_CH_ClientSummary chs ON sys.ResourceID = chs.ResourceID
{whereClause}
ORDER BY 
    sys.Client0 DESC,
    sys.Decommissioned0 ASC,
    bgb.OnlineStatus DESC,
    chs.LastPolicyRequest DESC,
    bgb.LastOnlineTime DESC";

                    DataTable devicesTable = databaseContext.QueryService.ExecuteTable(query);

                    if (devicesTable.Rows.Count == 0)
                    {
                        Logger.NewLine();
                        Logger.Warning("No devices found");
                        continue;
                    }

                    // Decode PrimaryGroupRID to human-readable names
                    if (devicesTable.Columns.Contains("PrimaryGroupRID"))
                    {
                        DataColumn decodedGroupColumn = devicesTable.Columns.Add("PrimaryGroup", typeof(string));
                        int groupRidIndex = devicesTable.Columns["PrimaryGroupRID"].Ordinal;
                        decodedGroupColumn.SetOrdinal(groupRidIndex);

                        foreach (DataRow row in devicesTable.Rows)
                        {
                            if (row["PrimaryGroupRID"] != DBNull.Value)
                            {
                                string rid = row["PrimaryGroupRID"].ToString();
                                row["PrimaryGroup"] = rid switch
                                {
                                    "513" => "Domain Users",
                                    "514" => "Domain Guests",
                                    "515" => "Domain Computers",
                                    "516" => "Domain Controllers",
                                    "517" => "Cert Publishers",
                                    "518" => "Schema Admins",
                                    "519" => "Enterprise Admins",
                                    "520" => "Group Policy Creator Owners",
                                    "521" => "Read-Only Domain Controllers",
                                    "522" => "Cloneable Domain Controllers",
                                    "525" => "Protected Users",
                                    "526" => "Key Admins",
                                    "527" => "Enterprise Key Admins",
                                    _ => $"RID {rid}"
                                };
                            }
                        }
                        devicesTable.Columns.Remove("PrimaryGroupRID");
                    }

                    // Decode UserAccountControl flags
                    if (devicesTable.Columns.Contains("UserAccountControl"))
                    {
                        DataColumn decodedUacColumn = devicesTable.Columns.Add("AccountStatus", typeof(string));
                        int uacIndex = devicesTable.Columns["UserAccountControl"].Ordinal;
                        decodedUacColumn.SetOrdinal(uacIndex);

                        foreach (DataRow row in devicesTable.Rows)
                        {
                            if (row["UserAccountControl"] != DBNull.Value && int.TryParse(row["UserAccountControl"].ToString(), out int uacValue))
                            {
                                var flags = new System.Collections.Generic.List<string>();
                                
                                if ((uacValue & 0x0002) != 0) flags.Add("Disabled");
                                else flags.Add("Enabled");

                                if ((uacValue & 0x0020) != 0) flags.Add("NoPassword");
                                if ((uacValue & 0x0040) != 0) flags.Add("PwdCantChange");
                                if ((uacValue & 0x0080) != 0) flags.Add("EncryptedPwd");
                                if ((uacValue & 0x0200) != 0) flags.Add("NormalAccount");
                                if ((uacValue & 0x0800) != 0) flags.Add("InterdomainTrust");
                                if ((uacValue & 0x1000) != 0) flags.Add("WorkstationTrust");
                                if ((uacValue & 0x2000) != 0) flags.Add("ServerTrust");
                                if ((uacValue & 0x10000) != 0) flags.Add("PwdNeverExpires");
                                if ((uacValue & 0x20000) != 0) flags.Add("LockedOut");
                                if ((uacValue & 0x40000) != 0) flags.Add("PwdExpired");
                                if ((uacValue & 0x80000) != 0) flags.Add("TrustedForDelegation");

                                row["AccountStatus"] = string.Join(", ", flags);
                            }
                        }
                        devicesTable.Columns.Remove("UserAccountControl");
                    }

                    // Decode Virtual_Machine_Type
                    if (devicesTable.Columns.Contains("VMTypeRaw"))
                    {
                        DataColumn decodedVmTypeColumn = devicesTable.Columns.Add("VMType", typeof(string));
                        int vmTypeIndex = devicesTable.Columns["VMTypeRaw"].Ordinal;
                        decodedVmTypeColumn.SetOrdinal(vmTypeIndex);

                        foreach (DataRow row in devicesTable.Rows)
                        {
                            if (row["VMTypeRaw"] != DBNull.Value && int.TryParse(row["VMTypeRaw"].ToString(), out int vmType))
                            {
                                row["VMType"] = vmType switch
                                {
                                    0 => "Physical",
                                    1 => "Hyper-V",
                                    2 => "VMware",
                                    3 => "Xen",
                                    4 => "VirtualBox",
                                    _ => $"Unknown ({vmType})"
                                };
                            }
                        }
                        devicesTable.Columns.Remove("VMTypeRaw");
                    }

                    // Decode ManagementAuthority
                    if (devicesTable.Columns.Contains("ManagementAuthority"))
                    {
                        foreach (DataRow row in devicesTable.Rows)
                        {
                            if (row["ManagementAuthority"] != DBNull.Value && int.TryParse(row["ManagementAuthority"].ToString(), out int authority))
                            {
                                row["ManagementAuthority"] = authority switch
                                {
                                    0 => "ConfigMgr",
                                    1 => "Intune",
                                    2 => "Co-Managed",
                                    4 => "EAS (Exchange)",
                                    _ => $"Unknown ({authority})"
                                };
                            }
                        }
                    }

                    // Add UniqueCollections column - shows collections NOT shared by all devices
                    devicesTable.Columns.Add("UniqueCollections", typeof(string));

                    // Only compute unique collections if there are multiple devices
                    if (devicesTable.Rows.Count > 1)
                    {
                        var collectionCounts = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        var deviceCollections = new System.Collections.Generic.List<string[]>(devicesTable.Rows.Count);
                        int totalDevices = devicesTable.Rows.Count;

                        // Single pass: count collections and store splits
                        foreach (DataRow row in devicesTable.Rows)
                        {
                            string collectionsStr = row["Collections"]?.ToString() ?? "";
                            string[] collections = string.IsNullOrEmpty(collectionsStr) 
                                ? Array.Empty<string>() 
                                : collectionsStr.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                            
                            deviceCollections.Add(collections);

                            foreach (var collection in collections)
                            {
                                if (collectionCounts.ContainsKey(collection))
                                    collectionCounts[collection]++;
                                else
                                    collectionCounts[collection] = 1;
                            }
                        }

                        // Find collections that are NOT in all devices
                        var uniqueCollectionNames = new System.Collections.Generic.HashSet<string>(
                            collectionCounts.Where(kvp => kvp.Value < totalDevices).Select(kvp => kvp.Key),
                            StringComparer.OrdinalIgnoreCase
                        );

                        // Populate UniqueCollections using pre-split arrays
                        for (int i = 0; i < devicesTable.Rows.Count; i++)
                        {
                            var collections = deviceCollections[i];
                            if (collections.Length > 0)
                            {
                                var uniqueOnes = collections.Where(c => uniqueCollectionNames.Contains(c));
                                devicesTable.Rows[i]["UniqueCollections"] = string.Join(", ", uniqueOnes);
                            }
                        }
                    }

                    // If dataTables is not empty
                    if (devicesTable.Rows.Count > 0)
                    {
                        Console.WriteLine(OutputFormatter.ConvertDataTable(devicesTable));

                        Logger.Success($"Found {devicesTable.Rows.Count} device(s)");
                        Logger.SuccessNested("Use 'cm-device-users' to see detailed user logon statistics per device");
                    }
                    else {
                        Logger.Warning("No devices found");
                    }

                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to enumerate devices: {ex.Message}");
                }
            }

            return null;
        }
    }
}
