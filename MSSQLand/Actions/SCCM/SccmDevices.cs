using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.SCCM
{
    /// <summary>
    /// List known SCCM devices with ResourceID, name, online status, and last activity.
    /// Queries v_R_System and BGB_ResStatus tables.
    /// </summary>
    internal class SccmDevices : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "f", LongName = "filter", Description = "Filter by name, IP, or username")]
        private string _filter = "";

        [ArgumentMetadata(Position = 1, ShortName = "o", LongName = "online", Description = "Show only online devices (default: false)")]
        private bool _onlineOnly = false;

        [ArgumentMetadata(Position = 2, ShortName = "l", LongName = "limit", Description = "Limit number of results (default: 50)")]
        private int _limit = 50;

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            _filter = GetNamedArgument(named, "f", null)
                   ?? GetNamedArgument(named, "filter", null)
                   ?? GetPositionalArgument(positional, 0, "");

            string onlineStr = GetNamedArgument(named, "o", null)
                            ?? GetNamedArgument(named, "online", null);
            if (!string.IsNullOrEmpty(onlineStr))
            {
                _onlineOnly = bool.Parse(onlineStr);
            }

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
            string onlineMsg = _onlineOnly ? " (online only)" : "";
            Logger.TaskNested($"Enumerating SCCM devices{filterMsg}{onlineMsg}");

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
                    
                    // Add filter conditions (name, IP, username, domain)
                    if (!string.IsNullOrEmpty(_filter))
                    {
                        whereClause += $" AND (sys.Name0 LIKE '%{_filter.Replace("'", "''")}%' " +
                                      $"OR sys.User_Name0 LIKE '%{_filter.Replace("'", "''")}%' " +
                                      $"OR sys.Resource_Domain_OR_Workgr0 LIKE '%{_filter.Replace("'", "''")}%' " +
                                      $"OR SYSIP.IP_Addresses0 LIKE '{_filter.Replace("'", "''")}%')";
                    }

                    // Add online status filter
                    if (_onlineOnly)
                    {
                        whereClause += " AND bgb.OnlineStatus = 1";
                    }

                    string topClause = _limit > 0 ? $"TOP {_limit}" : "";

                    string query = $@"
SELECT {topClause}
    sys.ResourceID,
    sys.Name0 AS DeviceName,
    sys.Resource_Domain_OR_Workgr0 AS Domain,
    sys.SMS_Unique_Identifier0 AS SMSID,
    sys.Operating_System_Name_and0 AS OperatingSystem,
    sys.User_Name0 AS LastUser,
    sys.AD_Site_Name0 AS ADSite,
    bgb.OnlineStatus,
    bgb.LastOnlineTime,
    sys.Last_Logon_Timestamp0 AS LastLogon,
    sys.Client_Version0 AS ClientVersion,
    sys.Client0 AS Client,
    sys.Decommissioned0 AS Decommissioned
FROM [{db}].dbo.v_R_System sys
LEFT JOIN [{db}].dbo.BGB_ResStatus bgb ON sys.ResourceID = bgb.ResourceID
LEFT JOIN [{db}].dbo.v_RA_System_IPAddresses SYSIP ON sys.ResourceID = SYSIP.ResourceID
{whereClause}
GROUP BY sys.ResourceID, sys.Name0, sys.Resource_Domain_OR_Workgr0, sys.SMS_Unique_Identifier0,
         sys.Operating_System_Name_and0, sys.User_Name0, sys.AD_Site_Name0, bgb.OnlineStatus,
         bgb.LastOnlineTime, sys.Last_Logon_Timestamp0, sys.Client_Version0, sys.Client0, sys.Decommissioned0
ORDER BY 
    bgb.OnlineStatus,
    sys.Resource_Domain_OR_Workgr0,
    bgb.LastOnlineTime DESC";

                    DataTable devicesTable = databaseContext.QueryService.ExecuteTable(query);

                    if (devicesTable.Rows.Count == 0)
                    {
                        Logger.Warning("No devices found");
                        continue;
                    }

                    Logger.Success($"Found {devicesTable.Rows.Count} device(s)");

                    Console.WriteLine(OutputFormatter.ConvertDataTable(devicesTable));
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
