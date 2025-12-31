using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.SCCM
{
    /// <summary>
    /// List SCCM devices with BGB (Background) notification channel status.
    /// Shows online/offline status, last contact times, and Management Point access.
    /// </summary>
    internal class SccmDevicesBgb : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "f", LongName = "filter", Description = "Filter by name or IP address")]
        private string _filter = "";

        [ArgumentMetadata(Position = 1, ShortName = "l", LongName = "limit", Description = "Limit number of results (default: 50)")]
        private int _limit = 50;

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            _filter = GetNamedArgument(named, "f", null)
                   ?? GetNamedArgument(named, "filter", null)
                   ?? GetPositionalArgument(positional, 0, "");

            string limitStr = GetNamedArgument(named, "l", null)
                           ?? GetNamedArgument(named, "limit", null)
                           ?? GetPositionalArgument(positional, 1);
            if (!string.IsNullOrEmpty(limitStr))
            {
                _limit = int.Parse(limitStr);
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            string filterMsg = !string.IsNullOrEmpty(_filter) ? $" (filter: {_filter})" : "";
            Logger.TaskNested($"Enumerating SCCM devices BGB status{filterMsg}");

            SccmService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            string[] requiredTables = { "v_R_System", "BGB_ResStatus" };
            var databases = sccmService.GetValidatedSccmDatabases(requiredTables, 2);

            if (databases.Count == 0)
            {
                Logger.Warning("No SCCM databases found");
                return null;
            }

            foreach (string db in databases)
            {
                string siteCode = SccmService.GetSiteCode(db);
                Logger.Info($"SCCM database: {db} (Site Code: {siteCode})");

                try
                {
                    string whereClause = "WHERE 1=1";
                    
                    if (!string.IsNullOrEmpty(_filter))
                    {
                        whereClause += $" AND (sys.Name0 LIKE '%{_filter.Replace("'", "''")}%' " +
                                      $"OR brs.IPAddress LIKE '{_filter.Replace("'", "''")}%')";
                    }

                    string topClause = _limit > 0 ? $"TOP {_limit}" : "";

                    string query;
                    if (databaseContext.QueryService.ExecutionServer.IsLegacy)
                    {
                        // SQL Server 2016 and earlier: Use STUFF + FOR XML PATH
                        query = $@"
SELECT {topClause}
    sys.ResourceID,
    sys.Name0 AS DeviceName,
    CASE 
        WHEN brs.OnlineStatus = 1 THEN 'Online'
        WHEN brs.OnlineStatus = 0 THEN 'Offline'
        ELSE 'Unknown'
    END AS OnlineStatus,
    brs.LastOnlineTime,
    brs.LastOfflineTime,
    brs.IPAddress,
    brs.AccessMP,
    STUFF((
        SELECT ', ' + col.Name
        FROM [{db}].dbo.v_FullCollectionMembership cm
        INNER JOIN [{db}].dbo.v_Collection col ON cm.CollectionID = col.CollectionID
        WHERE cm.ResourceID = sys.ResourceID AND col.CollectionType = 2
        FOR XML PATH(''), TYPE
    ).value('.', 'NVARCHAR(MAX)'), 1, 2, '') AS Collections
FROM [{db}].dbo.v_R_System sys
INNER JOIN [{db}].dbo.BGB_ResStatus brs ON sys.ResourceID = brs.ResourceID
{whereClause}
GROUP BY sys.ResourceID, sys.Name0, brs.OnlineStatus, brs.LastOnlineTime, brs.LastOfflineTime, brs.IPAddress, brs.AccessMP
ORDER BY brs.LastOnlineTime DESC";
                    }
                    else
                    {
                        // SQL Server 2017+: Use STRING_AGG
                        query = $@"
SELECT {topClause}
    sys.ResourceID,
    sys.Name0 AS DeviceName,
    CASE 
        WHEN brs.OnlineStatus = 1 THEN 'Online'
        WHEN brs.OnlineStatus = 0 THEN 'Offline'
        ELSE 'Unknown'
    END AS OnlineStatus,
    brs.LastOnlineTime,
    brs.LastOfflineTime,
    brs.IPAddress,
    brs.AccessMP,
    STRING_AGG(col.Name, ', ') AS Collections
FROM [{db}].dbo.v_R_System sys
INNER JOIN [{db}].dbo.BGB_ResStatus brs ON sys.ResourceID = brs.ResourceID
LEFT JOIN [{db}].dbo.v_FullCollectionMembership cm ON sys.ResourceID = cm.ResourceID
LEFT JOIN [{db}].dbo.v_Collection col ON cm.CollectionID = col.CollectionID AND col.CollectionType = 2
{whereClause}
GROUP BY sys.ResourceID, sys.Name0, brs.OnlineStatus, brs.LastOnlineTime, brs.LastOfflineTime, brs.IPAddress, brs.AccessMP
ORDER BY brs.LastOnlineTime DESC";
                    }

                    DataTable statusTable = databaseContext.QueryService.ExecuteTable(query);

                    if (statusTable.Rows.Count == 0)
                    {
                        Logger.Warning("No devices found");
                        continue;
                    }

                    Logger.Success($"Found {statusTable.Rows.Count} device(s) with BGB status");
                    Logger.NewLine();

                    Console.WriteLine(OutputFormatter.ConvertDataTable(statusTable));
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to enumerate BGB status: {ex.Message}");
                }
            }

            return null;
        }
    }
}
