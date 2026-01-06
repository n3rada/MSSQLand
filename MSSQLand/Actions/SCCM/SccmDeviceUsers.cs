using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.SCCM
{
    /// <summary>
    /// Shows all users who have logged into SCCM-managed devices with usage statistics
    /// </summary>
    internal class SccmDeviceUsers : BaseAction
    {
        [ArgumentMetadata(Position = 0, LongName = "device", Description = "Filter by device name")]
        private string _device = "";

        [ArgumentMetadata(Position = 1, LongName = "username", Description = "Filter by username")]
        private string _username = "";

        [ArgumentMetadata(Position = 2, LongName = "limit", Description = "Limit number of results (default: 50)")]
        private int _limit = 50;

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            _device = GetNamedArgument(named, "device", null)
                   ?? GetPositionalArgument(positional, 0, "");

            _username = GetNamedArgument(named, "username", null)
                     ?? GetPositionalArgument(positional, 1, "");

            string limitStr = GetNamedArgument(named, "limit", null)
                           ?? GetPositionalArgument(positional, 2);
            if (!string.IsNullOrEmpty(limitStr))
            {
                _limit = int.Parse(limitStr);
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            string deviceMsg = !string.IsNullOrEmpty(_device) ? $" (device: {_device})" : "";
            string usernameMsg = !string.IsNullOrEmpty(_username) ? $" (username: {_username})" : "";
            Logger.TaskNested($"Enumerating SCCM device users{deviceMsg}{usernameMsg}");
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

                    // Add username filter
                    if (!string.IsNullOrEmpty(_username))
                    {
                        whereClause += $" AND cu.SystemConsoleUser0 LIKE '%{_username.Replace("'", "''")}%'";
                    }

                    string topClause = _limit > 0 ? $"TOP {_limit}" : "";

                    string query = $@"
SELECT {topClause}
    sys.Name0 AS DeviceName,
    sys.Resource_Domain_OR_Workgr0 AS Domain,
    cu.SystemConsoleUser0 AS Username,
    cu.NumberOfConsoleLogons0 AS TotalLogons,
    cu.TotalUserConsoleMinutes0 AS TotalMinutes,
    cu.LastConsoleUse0 AS LastUsed,
    cu.TimeStamp AS LastInventory
FROM [{db}].dbo.v_R_System sys
INNER JOIN [{db}].dbo.v_GS_SYSTEM_CONSOLE_USER cu ON sys.ResourceID = cu.ResourceID
{whereClause}
ORDER BY 
    sys.Name0,
    cu.TotalUserConsoleMinutes0 DESC";

                    DataTable usersTable = databaseContext.QueryService.ExecuteTable(query);

                    if (usersTable.Rows.Count == 0)
                    {
                        Logger.NewLine();
                        Logger.Warning("No device users found");
                        continue;
                    }

                    Console.WriteLine(OutputFormatter.ConvertDataTable(usersTable));

                    Logger.Success($"Found {usersTable.Rows.Count} device-user relationship(s)");

                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to enumerate device users: {ex.Message}");
                }
            }

            return null;
        }
    }
}
