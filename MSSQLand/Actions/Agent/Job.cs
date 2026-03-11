// MSSQLand/Actions/Agent/Job.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.Agent
{
    /// <summary>
    /// Display detailed information about a specific SQL Server Agent job including all steps,
    /// commands, execution history, schedule, and proxy usage.
    /// Queries msdb.dbo.sysjobs, sysjobsteps, sysjobhistory, sysjobschedules, sysschedules, sysproxies.
    /// </summary>
    internal class Job : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Job name or job_id GUID to inspect")]
        private string _jobIdentifier = "";

        [ArgumentMetadata(ShortName = "l", LongName = "limit", Description = "Max history rows to return (default: 25)")]
        private int _historyLimit = 25;

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);

            if (string.IsNullOrWhiteSpace(_jobIdentifier))
            {
                throw new ArgumentException("Job name or job_id is required. Example: job 'Full Backup'");
            }
        }

        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Retrieving details for job: {_jobIdentifier}");

            // Determine if identifier is a GUID or name
            bool isGuid = Guid.TryParse(_jobIdentifier, out _);
            string jobFilter = isGuid
                ? $"j.job_id = '{_jobIdentifier.Replace("'", "''")}'"
                : $"j.name = '{_jobIdentifier.Replace("'", "''")}'";

            // ── Job metadata ──
            string jobQuery = $@"
                SELECT
                    j.job_id,
                    j.name AS JobName,
                    SUSER_SNAME(j.owner_sid) AS Owner,
                    j.enabled AS Enabled,
                    c.name AS Category,
                    j.description AS Description,
                    j.date_created AS Created,
                    j.date_modified AS Modified,
                    j.notify_level_eventlog,
                    j.notify_level_email,
                    j.delete_level
                FROM msdb.dbo.sysjobs j
                LEFT JOIN msdb.dbo.syscategories c
                    ON j.category_id = c.category_id
                WHERE {jobFilter};";

            DataTable jobInfo = databaseContext.QueryService.ExecuteTable(jobQuery);

            if (jobInfo.Rows.Count == 0)
            {
                Logger.Error($"Job not found: {_jobIdentifier}");
                return null;
            }

            DataRow job = jobInfo.Rows[0];
            string jobId = job["job_id"].ToString();
            string jobName = job["JobName"].ToString();

            Logger.NewLine();
            Logger.Info($"{jobName} ({jobId})");
            Logger.InfoNested($"Owner: {job["Owner"]}");
            Logger.InfoNested($"Enabled: {job["Enabled"]}");
            Logger.InfoNested($"Category: {job["Category"]}");

            string description = job["Description"]?.ToString();
            if (!string.IsNullOrWhiteSpace(description) && description != "No description available.")
            {
                Logger.InfoNested($"Description: {description}");
            }

            Logger.InfoNested($"Created: {job["Created"]}");
            Logger.InfoNested($"Modified: {job["Modified"]}");

            // ── Job steps ──
            Logger.NewLine();
            Logger.Info("Job Steps");

            string stepsQuery = $@"
                SELECT
                    js.step_id AS StepId,
                    js.step_name AS StepName,
                    js.subsystem AS Subsystem,
                    js.database_name AS [Database],
                    js.database_user_name AS DatabaseUser,
                    js.on_success_action AS OnSuccess,
                    js.on_fail_action AS OnFail,
                    js.retry_attempts AS RetryAttempts,
                    js.retry_interval AS RetryInterval,
                    js.last_run_outcome AS LastRunOutcome,
                    js.last_run_duration AS LastRunDuration,
                    js.last_run_date AS LastRunDate,
                    js.last_run_time AS LastRunTime,
                    js.output_file_name AS OutputFile,
                    p.name AS ProxyName,
                    js.command AS Command
                FROM msdb.dbo.sysjobsteps js
                LEFT JOIN msdb.dbo.sysproxies p
                    ON js.proxy_id = p.proxy_id
                WHERE js.job_id = '{jobId}'
                ORDER BY js.step_id;";

            DataTable steps = databaseContext.QueryService.ExecuteTable(stepsQuery);

            if (steps.Rows.Count == 0)
            {
                Logger.InfoNested("No steps defined");
            }
            else
            {
                // Display steps table without Command column
                DataTable stepsDisplay = steps.Copy();
                stepsDisplay.Columns.Remove("Command");
                Console.WriteLine(OutputFormatter.ConvertDataTable(stepsDisplay));
                Logger.Success($"Found {steps.Rows.Count} step(s)");

                // Display each step's command separately
                foreach (DataRow step in steps.Rows)
                {
                    string command = step["Command"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(command))
                    {
                        Logger.NewLine();
                        Logger.Info($"Step {step["StepId"]} Command ({step["StepName"]})");
                        Console.WriteLine(command);
                    }
                }
            }

            // ── Schedules ──
            Logger.NewLine();
            Logger.Info("Schedules");

            string scheduleQuery = $@"
                SELECT
                    s.name AS ScheduleName,
                    s.enabled AS Enabled,
                    CASE s.freq_type
                        WHEN 1  THEN 'Once'
                        WHEN 4  THEN 'Daily'
                        WHEN 8  THEN 'Weekly'
                        WHEN 16 THEN 'Monthly'
                        WHEN 32 THEN 'Monthly (relative)'
                        WHEN 64 THEN 'Agent start'
                        WHEN 128 THEN 'Idle'
                        ELSE CAST(s.freq_type AS VARCHAR)
                    END AS Frequency,
                    s.freq_interval AS FreqInterval,
                    s.active_start_date AS StartDate,
                    s.active_start_time AS StartTime,
                    s.active_end_date AS EndDate
                FROM msdb.dbo.sysjobschedules jsc
                JOIN msdb.dbo.sysschedules s
                    ON jsc.schedule_id = s.schedule_id
                WHERE jsc.job_id = '{jobId}';";

            DataTable schedules = databaseContext.QueryService.ExecuteTable(scheduleQuery);

            if (schedules.Rows.Count == 0)
            {
                Logger.InfoNested("No schedules assigned");
            }
            else
            {
                Console.WriteLine(OutputFormatter.ConvertDataTable(schedules));
                Logger.Success($"Found {schedules.Rows.Count} schedule(s)");
            }

            // ── Recent history ──
            Logger.NewLine();
            Logger.Info(_historyLimit > 0 ? $"Recent History (last {_historyLimit})" : "Recent History (all)");

            string historyTopClause = BuildTopClause(_historyLimit);

            string historyQuery = $@"
                SELECT {historyTopClause}
                    h.step_id AS StepId,
                    h.step_name AS StepName,
                    h.run_status AS RunStatus,
                    h.run_date AS RunDate,
                    h.run_time AS RunTime,
                    h.run_duration AS Duration,
                    h.retries_attempted AS Retries,
                    h.message AS Message
                FROM msdb.dbo.sysjobhistory h
                WHERE h.job_id = '{jobId}'
                ORDER BY h.run_date DESC, h.run_time DESC;";

            DataTable history = databaseContext.QueryService.ExecuteTable(historyQuery);

            if (history.Rows.Count == 0)
            {
                Logger.InfoNested("No execution history");
            }
            else
            {
                Console.WriteLine(OutputFormatter.ConvertDataTable(history));
                Logger.Success($"Found {history.Rows.Count} history record(s)");
            }

            return jobInfo;
        }
    }
}
