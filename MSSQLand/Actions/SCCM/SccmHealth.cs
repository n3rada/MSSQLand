using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.SCCM
{
    /// <summary>
    /// Display SCCM client health diagnostics and communication status.
    /// Use this for troubleshooting client issues: check-in times, inventory cycles, health evaluation results.
    /// Shows when devices last contacted SCCM, inventory scan times, and policy request status.
    /// For general device inventory and discovery, use sccm-devices instead.
    /// </summary>
    internal class SccmHealth : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "f", LongName = "filter", Description = "Filter by device name")]
        private string _filter = "";

        [ArgumentMetadata(Position = 1,  LongName = "limit", Description = "Limit number of results (default: 50)")]
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
            Logger.TaskNested($"Enumerating SCCM client health{filterMsg}");
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
                    
                    if (!string.IsNullOrEmpty(_filter))
                    {
                        whereClause += $" AND sys.Name0 LIKE '%{_filter.Replace("'", "''")}%'";
                    }

                    string topClause = _limit > 0 ? $"TOP {_limit}" : "";

                    string query = $@"
SELECT {topClause}
    sys.ResourceID,
    sys.Name0 AS DeviceName,
    sys.Resource_Domain_OR_Workgr0 AS Domain,
    ch.ClientActiveStatus,
    ch.ClientState,
    ch.ClientStateDescription,
    ch.LastActiveTime,
    ch.LastOnline,
    ch.LastDDR,
    ch.LastHW AS LastHardwareScan,
    ch.LastSW AS LastSoftwareScan,
    ch.LastPolicyRequest,
    ch.LastStatusMessage,
    ch.LastHealthEvaluation,
    ch.LastHealthEvaluationResult,
    ch.LastEvaluationHealthy,
    ch.IsActiveDDR,
    ch.IsActiveHW,
    ch.IsActiveSW,
    ch.IsActivePolicyRequest,
    ch.IsActiveStatusMessages,
    us.LastScanTime AS LastUpdateScanTime,
    us.LastErrorCode AS UpdateScanErrorCode,
    us.LastScanPackageLocation AS UpdateScanLocation,
    us.LastWUAVersion AS WindowsUpdateAgent
FROM [{db}].dbo.v_R_System sys
LEFT JOIN [{db}].dbo.v_CH_ClientSummary ch ON sys.ResourceID = ch.ResourceID
LEFT JOIN [{db}].dbo.v_UpdateScanStatus us ON sys.ResourceID = us.ResourceID
{whereClause}
ORDER BY ch.LastActiveTime DESC";

                    DataTable healthTable = databaseContext.QueryService.ExecuteTable(query);

                    if (healthTable.Rows.Count == 0)
                    {
                        Logger.NewLine();
                        Logger.Warning("No health data found");
                        continue;
                    }

                    Console.WriteLine(OutputFormatter.ConvertDataTable(healthTable));

                    Logger.Success($"Found {healthTable.Rows.Count} device(s) with health data");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to enumerate health data: {ex.Message}");
                }
            }

            return null;
        }
    }
}
