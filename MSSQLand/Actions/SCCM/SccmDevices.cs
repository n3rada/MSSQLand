using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;
using System.Linq;

namespace MSSQLand.Actions.SCCM
{
    /// <summary>
    /// </summary>
    internal class SccmDevices : BaseAction
    {
        [ArgumentMetadata(Position = 0, LongName = "device", Description = "Filter by device name")]
        private string _device = "";

        [ArgumentMetadata(Position = 1, ShortName = "d", LongName = "domain", Description = "Filter by domain")]
        private string _domain = "";

        [ArgumentMetadata(Position = 2, ShortName = "u", LongName = "username", Description = "Filter by username")]
        private string _username = "";

        [ArgumentMetadata(Position = 3, ShortName = "i", LongName = "ip", Description = "Filter by IP address")]
        private string _ip = "";

        [ArgumentMetadata(Position = 4, ShortName = "c", LongName = "collection", Description = "Filter by collection name")]
        private string _collection = "";

        [ArgumentMetadata(Position = 5, ShortName = "o", LongName = "online", Description = "Show only online devices (default: false)")]
        private bool _onlineOnly = false;

        [ArgumentMetadata(Position = 6, LongName = "require-lastuser", Description = "Show only devices with a LastUser value (default: false)")]
        private bool _requireLastUser = false;

        [ArgumentMetadata(Position = 7, ShortName = "n", LongName = "no-user", Description = "Show only devices without a LastUser value (default: false)")]
        private bool _noUser = false;

        [ArgumentMetadata(Position = 8, LongName = "client-only", Description = "Show only devices with SCCM client installed (default: false)")]
        private bool _clientOnly = false;

        [ArgumentMetadata(Position = 9, LongName = "active", Description = "Show only active (non-decommissioned) devices (default: false)")]
        private bool _activeOnly = false;

        [ArgumentMetadata(Position = 10, LongName = "last-seen-days", Description = "Show devices seen online in last N days")]
        private int _lastSeenDays = 0;

        [ArgumentMetadata(Position = 11, ShortName = "l", LongName = "limit", Description = "Limit number of results (default: 50)")]
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
            string onlineMsg = _onlineOnly ? " (online only)" : "";
            string lastUserMsg = _requireLastUser ? " (with last user)" : "";
            string noUserMsg = _noUser ? " (no user)" : "";
            string clientOnlyMsg = _clientOnly ? " (client only)" : "";
            string activeOnlyMsg = _activeOnly ? " (active only)" : "";
            string lastSeenMsg = _lastSeenDays > 0 ? $" (seen in last {_lastSeenDays} days)" : "";
            Logger.TaskNested($"Enumerating SCCM devices{deviceMsg}{domainMsg}{usernameMsg}{ipMsg}{collectionMsg}{onlineMsg}{lastUserMsg}{noUserMsg}{clientOnlyMsg}{activeOnlyMsg}{lastSeenMsg}");
            Logger.TaskNested($"Limit: {_limit}");

            SccmService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            var databases = sccmService.GetSccmDatabases();

            if (databases.Count == 0)
            {
                Logger.Warning("No SCCM databases found");
                return null;
            }

            foreach (string db in databases)
            {
                Logger.NewLine();
                string siteCode = SccmService.GetSiteCode(db);
                Logger.Info($"SCCM database: {db} (Site Code: {siteCode})");

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

                    // Add username filter
                    if (!string.IsNullOrEmpty(_username))
                    {
                        whereClause += $" AND sys.User_Name0 LIKE '%{_username.Replace("'", "''")}%'";
                    }

                    // Add IP address filter
                    if (!string.IsNullOrEmpty(_ip))
                    {
                        whereClause += $" AND SYSIP.IP_Addresses0 LIKE '{_ip.Replace("'", "''")}%'";
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

                    // Collections aggregation - different for SQL Server 2016 vs 2017+
                    string collectionsSelect;
                    string collectionsJoins;
                    string collectionsGroupBy;

                    if (databaseContext.QueryService.ExecutionServer.IsLegacy)
                    {
                        // SQL Server 2016 and earlier: Use STUFF + FOR XML PATH (no joins needed)
                        collectionsSelect = $@"STUFF((
        SELECT ', ' + col.Name
        FROM [{db}].dbo.v_FullCollectionMembership cm
        INNER JOIN [{db}].dbo.v_Collection col ON cm.CollectionID = col.CollectionID
        WHERE cm.ResourceID = sys.ResourceID AND col.CollectionType = 2
        FOR XML PATH(''), TYPE
    ).value('.', 'NVARCHAR(MAX)'), 1, 2, '') AS Collections";
                        collectionsJoins = "";
                        collectionsGroupBy = "";
                    }
                    else
                    {
                        // SQL Server 2017+: Use STRING_AGG (requires joins and GROUP BY)
                        collectionsSelect = "STRING_AGG(col.Name, ', ') AS Collections";
                        collectionsJoins = $@"
LEFT JOIN [{db}].dbo.v_FullCollectionMembership cm ON sys.ResourceID = cm.ResourceID
LEFT JOIN [{db}].dbo.v_Collection col ON cm.CollectionID = col.CollectionID AND col.CollectionType = 2";
                        collectionsGroupBy = "";
                    }

                    string query = $@"
SELECT {topClause}
    bgb.OnlineStatus,
    bgb.LastOnlineTime,
    sys.AD_Site_Name0 AS ADSite,
    sys.Resource_Domain_OR_Workgr0 AS Domain,
    sys.Name0 AS DeviceName,
    bgb.IPAddress,
    sys.User_Name0 AS LastUser,
    sys.Last_Logon_Timestamp0 AS LastLogon,
    sys.Operating_System_Name_and0 AS OperatingSystem,
    sys.Client0 AS Client,
    sys.Client_Version0 AS ClientVersion,
    sys.Decommissioned0 AS Decommissioned,
    sys.Creation_Date0 AS RegisteredDate,
    bgb.LastOfflineTime,
    bgb.AccessMP,
    sys.ResourceID,
    sys.SMS_Unique_Identifier0 AS SMSID,
    {collectionsSelect}
FROM [{db}].dbo.v_R_System sys
LEFT JOIN [{db}].dbo.BGB_ResStatus bgb ON sys.ResourceID = bgb.ResourceID
LEFT JOIN [{db}].dbo.v_RA_System_IPAddresses SYSIP ON sys.ResourceID = SYSIP.ResourceID{collectionsJoins}
{whereClause}
GROUP BY sys.ResourceID, sys.Name0, sys.Resource_Domain_OR_Workgr0, sys.SMS_Unique_Identifier0,
         sys.Operating_System_Name_and0, sys.User_Name0, sys.AD_Site_Name0, sys.Creation_Date0,
         bgb.OnlineStatus, bgb.LastOnlineTime, bgb.LastOfflineTime, bgb.IPAddress, bgb.AccessMP,
         sys.Last_Logon_Timestamp0, sys.Client_Version0, sys.Client0, sys.Decommissioned0{collectionsGroupBy}
ORDER BY 
    sys.Client0 DESC,
    sys.Decommissioned0 ASC,
    bgb.OnlineStatus DESC,
    bgb.LastOnlineTime DESC";

                    DataTable devicesTable = databaseContext.QueryService.ExecuteTable(query);

                    if (devicesTable.Rows.Count == 0)
                    {
                        Logger.NewLine();
                        Logger.Warning("No devices found");
                        continue;
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

                    Console.WriteLine(OutputFormatter.ConvertDataTable(devicesTable));

                    Logger.Success($"Found {devicesTable.Rows.Count} device(s)");

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
