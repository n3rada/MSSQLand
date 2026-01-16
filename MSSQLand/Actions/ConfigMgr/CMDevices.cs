// MSSQLand/Actions/ConfigMgr/CMDevices.cs

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
    /// InventoriedUsers column shows all users from hardware inventory with last seen date, logon count, and total minutes.
    /// Note: User data reflects periodic hardware inventory cycles (typically 24h-7d), not real-time sessions.
    /// For ConfigMgr client health diagnostics and troubleshooting, use sccm-health instead.
    /// </summary>
    internal class CMDevices : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "n", LongName = "name", Description = "Filter by device name")]
        private string _name = "";

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

        [ArgumentMetadata(Position = 7, LongName = "users", Description = "Show only devices with users (LastConsoleUser or InventoriedUsers) (default: false)")]
        private bool _withUsers = false;

        [ArgumentMetadata(Position = 8, LongName = "no-user", Description = "Show only devices without a LastUser value (default: false)")]
        private bool _noUser = false;

        [ArgumentMetadata(Position = 9, LongName = "client-only", Description = "Show only devices with ConfigMgr client installed (default: false)")]
        private bool _clientOnly = false;

        [ArgumentMetadata(Position = 10, LongName = "active", Description = "Show only active (non-decommissioned) devices (default: false)")]
        private bool _activeOnly = false;

        [ArgumentMetadata(Position = 11, LongName = "last-seen-days", Description = "Show devices seen online in last N days")]
        private int _lastSeenDays = 0;

        [ArgumentMetadata(Position = 12, LongName = "user-seen-days", Description = "Show devices with user activity in last N days")]
        private int _userSeenDays = 0;

        [ArgumentMetadata(Position = 13, LongName = "limit", Description = "Limit number of results (default: 25)")]
        private int _limit = 25;

        [ArgumentMetadata(Position = 14, LongName = "count", Description = "Count matching devices only (bypasses limit, no details)")]
        private bool _countOnly = false;

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);

            // Implicitly enable --users when --user filter is specified
            if (!string.IsNullOrEmpty(_username))
            {
                _withUsers = true;
            }
        }

        public override object Execute(DatabaseContext databaseContext)
        {
            string deviceMsg = !string.IsNullOrEmpty(_name) ? $" (device: {_name})" : "";
            string domainMsg = !string.IsNullOrEmpty(_domain) ? $" (domain: {_domain})" : "";
            string usernameMsg = !string.IsNullOrEmpty(_username) ? $" (username: {_username})" : "";
            string ipMsg = !string.IsNullOrEmpty(_ip) ? $" (ip: {_ip})" : "";
            string collectionMsg = !string.IsNullOrEmpty(_collection) ? $" (collection: {_collection})" : "";
            string dnMsg = !string.IsNullOrEmpty(_distinguishedName) ? $" (dn: {_distinguishedName})" : "";
            string onlineMsg = _onlineOnly ? " (online only)" : "";
            string withUsersMsg = _withUsers ? " (with users)" : "";
            string noUserMsg = _noUser ? " (no user)" : "";
            string clientOnlyMsg = _clientOnly ? " (client only)" : "";
            string activeOnlyMsg = _activeOnly ? " (active only)" : "";
            string lastSeenMsg = _lastSeenDays > 0 ? $" (seen in last {_lastSeenDays} days)" : "";
            string userSeenMsg = _userSeenDays > 0 ? $" (user seen in last {_userSeenDays} days)" : "";
            string countMsg = _countOnly ? " (count only)" : "";

            Logger.TaskNested($"Enumerating ConfigMgr devices{deviceMsg}{domainMsg}{usernameMsg}{ipMsg}{collectionMsg}{dnMsg}{onlineMsg}{withUsersMsg}{noUserMsg}{clientOnlyMsg}{activeOnlyMsg}{lastSeenMsg}{userSeenMsg}{countMsg}");
            if (!_countOnly) Logger.TaskNested($"Limit: {_limit}");

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
                    if (!string.IsNullOrEmpty(_name))
                    {
                        whereClause += $" AND sys.Name0 LIKE '%{_name.Replace("'", "''")}%'";
                    }

                    // Add domain filter
                    if (!string.IsNullOrEmpty(_domain))
                    {
                        whereClause += $" AND sys.Resource_Domain_OR_Workgr0 LIKE '%{_domain.Replace("'", "''")}%'";
                    }

                    // Add username filter (searches both LastConsoleUser and InventoriedUsers)
                    // When user-seen-days is specified, only use v_GS_SYSTEM_CONSOLE_USER since it has date info
                    if (!string.IsNullOrEmpty(_username))
                    {
                        if (_userSeenDays > 0)
                        {
                            // Only search in v_GS_SYSTEM_CONSOLE_USER when date filtering is required
                            whereClause += $@" AND EXISTS (
                                SELECT 1
                                FROM [{db}].dbo.v_GS_SYSTEM_CONSOLE_USER cu_filter
                                WHERE cu_filter.ResourceID = sys.ResourceID
                                AND cu_filter.SystemConsoleUser0 LIKE '%{_username.Replace("'", "''")}%'
                                AND cu_filter.LastConsoleUse0 >= DATEADD(DAY, -{_userSeenDays}, GETDATE())
                            )";
                        }
                        else
                        {
                            // Without date filtering, search both LastConsoleUser and InventoriedUsers
                            whereClause += $@" AND (
                                (sys.User_Name0 LIKE '%{_username.Replace("'", "''")}%' AND sys.User_Name0 IS NOT NULL AND sys.User_Name0 != '')
                                OR EXISTS (
                                    SELECT 1
                                    FROM [{db}].dbo.v_GS_SYSTEM_CONSOLE_USER cu_filter
                                    WHERE cu_filter.ResourceID = sys.ResourceID
                                    AND cu_filter.SystemConsoleUser0 LIKE '%{_username.Replace("'", "''")}%'
                                )
                            )";
                        }
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

                    // Add users filter (devices with LastConsoleUser OR InventoriedUsers)
                    // Skip if --user is specified since that filter already ensures users exist
                    if (_withUsers && string.IsNullOrEmpty(_username))
                    {
                        whereClause += $@" AND (
                            (sys.User_Name0 IS NOT NULL AND sys.User_Name0 != '')
                            OR EXISTS (
                                SELECT 1
                                FROM [{db}].dbo.v_GS_SYSTEM_CONSOLE_USER cu_users
                                WHERE cu_users.ResourceID = sys.ResourceID
                            )
                        )";
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

                    // Add user-seen-days filter (devices with user activity in last N days)
                    // Skip if --user is specified since it's already combined with that filter
                    if (_userSeenDays > 0 && string.IsNullOrEmpty(_username))
                    {
                        whereClause += $@" AND EXISTS (
                            SELECT 1
                            FROM [{db}].dbo.v_GS_SYSTEM_CONSOLE_USER cu_recent
                            WHERE cu_recent.ResourceID = sys.ResourceID
                            AND cu_recent.LastConsoleUse0 >= DATEADD(DAY, -{_userSeenDays}, GETDATE())
                        )";
                    }

                    // Count-only mode: get breakdown by domain and sum for total
                    if (_countOnly)
                    {
                        string domainQuery = $@"
SELECT 
    ISNULL(sys.Resource_Domain_OR_Workgr0, '(No Domain)') AS Domain,
    COUNT(DISTINCT sys.ResourceID) AS DeviceCount
FROM [{db}].dbo.v_R_System sys
LEFT JOIN [{db}].dbo.BGB_ResStatus bgb ON sys.ResourceID = bgb.ResourceID
LEFT JOIN [{db}].dbo.v_CH_ClientSummary chs ON sys.ResourceID = chs.ResourceID
{whereClause}
GROUP BY sys.Resource_Domain_OR_Workgr0
ORDER BY COUNT(DISTINCT sys.ResourceID) DESC";

                        DataTable domainTable = databaseContext.QueryService.ExecuteTable(domainQuery);
                        
                        int totalCount = 0;
                        foreach (DataRow row in domainTable.Rows)
                        {
                            totalCount += Convert.ToInt32(row["DeviceCount"]);
                        }
                        
                        Logger.Success($"Matching devices: {totalCount}");
                        
                        foreach (DataRow row in domainTable.Rows)
                        {
                            string domain = row["Domain"].ToString();
                            int domainCount = Convert.ToInt32(row["DeviceCount"]);
                            Logger.SuccessNested($"{domain}: {domainCount}");
                        }
                        continue;
                    }

                    string topClause = _limit > 0 ? $"TOP {_limit}" : "";

                    string query = $@"
SELECT DISTINCT {topClause}
    sys.Name0 AS DeviceName,
    sys.ResourceID,
    sys.Resource_Domain_OR_Workgr0 AS Domain,
    sys.Primary_Group_ID0 AS PrimaryGroupRID,
    sys.Distinguished_Name0 AS DistinguishedName,
    sys.User_Name0 AS LastConsoleUser,
    STUFF((
        SELECT ', ' + cu.SystemConsoleUser0
            + ' (' + CONVERT(VARCHAR(10), cu.LastConsoleUse0, 120)
            + ', ' + CAST(ISNULL(cu.NumberOfConsoleLogons0, 0) AS VARCHAR) + ' logons'
            + ', ' + CAST(ISNULL(cu.TotalUserConsoleMinutes0, 0) AS VARCHAR) + ' min)'
        FROM [{db}].dbo.v_GS_SYSTEM_CONSOLE_USER cu
        WHERE cu.ResourceID = sys.ResourceID
        ORDER BY cu.LastConsoleUse0 DESC
        FOR XML PATH(''), TYPE
    ).value('.', 'NVARCHAR(MAX)'), 1, 2, '') AS InventoriedUsers,
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
    bgb.OnlineStatus DESC,
    bgb.LastOnlineTime DESC,
    chs.LastPolicyRequest DESC,
    sys.Client0 DESC,
    sys.Decommissioned0 ASC";

                    DataTable devicesTable = databaseContext.QueryService.ExecuteTable(query);

                    if (devicesTable.Rows.Count == 0)
                    {
                        Logger.NewLine();
                        Logger.Warning("No devices found");
                        continue;
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
                        DataColumn decodedAuthorityColumn = devicesTable.Columns.Add("Management", typeof(string));
                        int authorityIndex = devicesTable.Columns["ManagementAuthority"].Ordinal;
                        decodedAuthorityColumn.SetOrdinal(authorityIndex);

                        foreach (DataRow row in devicesTable.Rows)
                        {
                            if (row["ManagementAuthority"] != DBNull.Value && int.TryParse(row["ManagementAuthority"].ToString(), out int authority))
                            {
                                row["Management"] = authority switch
                                {
                                    0 => "ConfigMgr",
                                    1 => "Intune",
                                    2 => "Co-Managed",
                                    4 => "EAS (Exchange)",
                                    _ => $"Unknown ({authority})"
                                };
                            }
                        }
                        devicesTable.Columns.Remove("ManagementAuthority");
                    }

                    // If dataTables is not empty
                    if (devicesTable.Rows.Count > 0)
                    {
                        Console.WriteLine(OutputFormatter.ConvertDataTable(devicesTable));

                        Logger.Success($"Found {devicesTable.Rows.Count} device(s)");
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
