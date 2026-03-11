// MSSQLand/Actions/Agent/JobHistory.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.Agent
{
    /// <summary>
    /// Display SQL Server Agent job execution history from msdb.dbo.sysjobhistory.
    /// Optionally filter by job name and limit the number of rows returned.
    /// </summary>
    internal class JobHistory : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "n", LongName = "name", Description = "Filter by job name (substring match)")]
        private string _name = "";

        [ArgumentMetadata(ShortName = "l", LongName = "limit", Description = "Max rows to return (default: 25)")]
        private int _limit = 25;

        [ArgumentMetadata(ShortName = "f", LongName = "failed", Description = "Show only failed executions (default: false)")]
        private bool _failedOnly = false;

        public override object Execute(DatabaseContext databaseContext)
        {
            string filterMsg = !string.IsNullOrEmpty(_name) ? $" for '{_name}'" : "";
            string failedMsg = _failedOnly ? " (failed only)" : "";
            Logger.TaskNested($"Retrieving Agent job history{filterMsg}{failedMsg}");

            string whereClause = "WHERE 1=1";

            if (!string.IsNullOrEmpty(_name))
            {
                whereClause += $" AND j.name LIKE '%{_name.Replace("'", "''")}%'";
            }

            if (_failedOnly)
            {
                whereClause += " AND h.run_status = 0";
            }

            string topClause = BuildTopClause(_limit);

            string query = $@"
                SELECT {topClause}
                    j.name AS JobName,
                    h.step_id AS StepId,
                    h.step_name AS StepName,
                    CASE h.run_status
                        WHEN 0 THEN 'Failed'
                        WHEN 1 THEN 'Succeeded'
                        WHEN 2 THEN 'Retry'
                        WHEN 3 THEN 'Cancelled'
                        WHEN 4 THEN 'Running'
                        ELSE CAST(h.run_status AS VARCHAR)
                    END AS RunStatus,
                    h.run_date AS RunDate,
                    h.run_time AS RunTime,
                    h.run_duration AS Duration,
                    h.sql_severity AS Severity,
                    h.retries_attempted AS Retries,
                    h.message AS Message
                FROM msdb.dbo.sysjobhistory h
                JOIN msdb.dbo.sysjobs j
                    ON h.job_id = j.job_id
                {whereClause}
                ORDER BY h.run_date DESC, h.run_time DESC;";

            DataTable result = databaseContext.QueryService.ExecuteTable(query);

            if (result.Rows.Count == 0)
            {
                Logger.Info("No execution history found.");
                return null;
            }

            Console.WriteLine(OutputFormatter.ConvertDataTable(result));
            Logger.Success($"Found {result.Rows.Count} history record(s)");

            return result;
        }
    }
}
