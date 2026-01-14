// MSSQLand/Actions/ConfigMgr/CMDistributionPoints.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.ConfigMgr
{
    /// <summary>
    /// Enumerate ConfigMgr distribution points with content library paths and network shares.
    /// Use this to identify servers hosting package content - primary targets for lateral movement and content poisoning.
    /// Shows DP server names, content share paths (e.g., \\\\server\\SCCMContentLib$), NAL paths, and DP group memberships.
    /// Distribution points store all deployed content and often have relaxed security for client access.
    /// Critical for content modification attacks and identifying high-value file shares.
    /// </summary>
    internal class CMDistributionPoints : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "s", LongName = "server", Description = "Filter by server name")]
        private string _server = "";

        [ArgumentMetadata(Position = 1, ShortName = "t", LongName = "type", Description = "Filter by DP type")]
        private string _type = "";

        [ArgumentMetadata(Position = 2, ShortName = "a", LongName = "active", Description = "Show only active DPs (default: false)")]
        private bool _activeOnly = false;

        [ArgumentMetadata(Position = 3, ShortName = "l", LongName = "limit", Description = "Limit number of results (default: 100)")]
        private int _limit = 25;

        public override object Execute(DatabaseContext databaseContext)
        {
            string serverMsg = !string.IsNullOrEmpty(_server) ? $" (server: {_server})" : "";
            string typeMsg = !string.IsNullOrEmpty(_type) ? $" (type: {_type})" : "";
            string activeMsg = _activeOnly ? " (active only)" : "";
            Logger.TaskNested($"Enumerating ConfigMgr distribution points{serverMsg}{typeMsg}{activeMsg}");
            Logger.TaskNested($"Limit: {_limit}");

            CMService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            var databases = sccmService.GetSccmDatabases();

            if (databases.Count == 0)
            {
                Logger.Warning("No ConfigMgr databases found");
                return null;
            }

            DataTable result = null;

            foreach (string db in databases)
            {
                string siteCode = CMService.GetSiteCode(db);

                Logger.NewLine();
                Logger.Info($"ConfigMgr database: {db} (Site Code: {siteCode})");

                string whereClause = "WHERE 1=1";

                if (!string.IsNullOrEmpty(_server))
                {
                    whereClause += $" AND (ServerName LIKE '%{_server.Replace("'", "''")}%' OR NALPath LIKE '%{_server.Replace("'", "''")}%')";
                }

                if (!string.IsNullOrEmpty(_type))
                {
                    whereClause += $" AND Type LIKE '%{_type.Replace("'", "''")}%'";
                }

                if (_activeOnly)
                {
                    whereClause += " AND IsActive = 1";
                }

                string topClause = _limit > 0 ? $"TOP {_limit}" : "";

                string query = $@"
SELECT {topClause}
    DPID,
    ServerName,
    NALPath,
    ShareName,
    SMSSiteCode,
    Type,
    State,
    CASE State
        WHEN 0 THEN 'Not Installed'
        WHEN 1 THEN 'Installed'
        WHEN 2 THEN 'Installation Failed'
        WHEN 3 THEN 'Installation Pending'
        ELSE CAST(State AS VARCHAR)
    END AS StateDescription,
    IsActive,
    IsPXE,
    IsPullDP,
    IsBITS,
    IsMulticast,
    DPDrive,
    MinFreeSpace,
    Description,
    Priority,
    TransferRate,
    MaintenanceMode,
    MaintenanceModeLastStartTime,
    AnonymousEnabled,
    TokenAuthEnabled,
    SslState,
    IsProtected,
    PreStagingAllowed,
    IsDoincEnabled,
    PreferredMPEnabled
FROM [{db}].dbo.DistributionPoints
{whereClause}
ORDER BY ServerName;";

                result = databaseContext.QueryService.ExecuteTable(query);

                if (result.Rows.Count == 0)
                {
                    Logger.Warning("No distribution points found");
                    continue;
                }

                Console.WriteLine(OutputFormatter.ConvertDataTable(result));

                Logger.Success($"Found {result.Rows.Count} distribution point(s)");
            }

            return result;
        }
    }
}
