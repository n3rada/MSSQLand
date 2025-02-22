using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;
using System.Linq;

namespace MSSQLand.Actions.Execution
{
    /// <summary>
    /// Executes SQL Server Agent actions (list jobs, execute commands).
    /// </summary>
    internal class Agents : BaseAction
    {
        private enum ActionMode { Status, Exec }
        private enum SubSystemMode { Cmd, PowerShell, TSQL, VBScript }

        private ActionMode _action = ActionMode.Status;
        private string? _command = null;
        private SubSystemMode _subSystem = SubSystemMode.PowerShell;

        public override void ValidateArguments(string additionalArguments)
        {
            string[] parts = SplitArguments(additionalArguments);

            if (parts.Length == 0)
            {
                return;
            }

            // Parse action mode
            if (!Enum.TryParse(parts[0].Trim(), true, out _action))
            {
                string validActions = string.Join(", ", Enum.GetNames(typeof(ActionMode)).Select(a => a.ToLower()));
                throw new ArgumentException($"Invalid action: {parts[0]}. Valid actions are: {validActions}.");
            }

            if (_action == ActionMode.Exec)
            {
                if (parts.Length < 2)
                {
                    throw new ArgumentException("Missing command to execute. Example: /a:agents exec 'whoami'");
                }

                _command = parts[1].Trim();

                // Optional: Parse subsystem
                if (parts.Length > 2 && !Enum.TryParse(parts[2].Trim(), true, out _subSystem))
                {
                    string validSubSystems = string.Join(", ", Enum.GetNames(typeof(SubSystemMode)).Select(s => s.ToLower()));
                    throw new ArgumentException($"Invalid subsystem: {parts[2]}. Valid subsystems are: {validSubSystems}.");
                }
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Executing {_action} mode");

            if (_action == ActionMode.Status)
            {
                return ListAgentJobs(databaseContext);
            }
            else if (_action == ActionMode.Exec)
            {
                return ExecuteAgentJob(databaseContext);
            }

            Logger.Error("Unknown execution mode.");
            return null;
        }

        /// <summary>
        /// Lists SQL Server Agent jobs.
        /// </summary>
        private object? ListAgentJobs(DatabaseContext databaseContext)
        {
            if (!AgentStatus(databaseContext))
            {
                return null;
            }

            Logger.Info("Retrieving SQL Server Agent Jobs...");

            string query = "SELECT job_id, name, enabled, date_created, date_modified FROM msdb.dbo.sysjobs ORDER BY date_created;";
            DataTable jobsTable = databaseContext.QueryService.ExecuteTable(query);

            if (jobsTable.Rows.Count == 0)
            {
                Logger.Info("No SQL Agent jobs found.");
                return null;
            }

            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(jobsTable));
            return jobsTable;
        }

        /// <summary>
        /// Executes a job via SQL Server Agent.
        /// </summary>
        private bool ExecuteAgentJob(DatabaseContext databaseContext)
        {
            if (string.IsNullOrEmpty(_command))
            {
                Logger.Warning("No command provided to execute.");
                return false;
            }

            Logger.TaskNested($"Executing command: {_command}");
            Logger.TaskNested($"Using subsystem: {_subSystem}");

            if (!AgentStatus(databaseContext))
            {
                return false;
            }

            string jobName = Guid.NewGuid().ToString("N").Substring(0, 6);
            string stepName = Guid.NewGuid().ToString("N").Substring(0, 6);

            Logger.Info($"Job name: {jobName}");
            Logger.Info($"Step name: {stepName}");

            string createJobQuery = $@"
                USE msdb;
                EXEC dbo.sp_add_job @job_name = '{jobName}';
                EXEC sp_add_jobstep @job_name = '{jobName}', @step_name = '{stepName}', 
                                    @subsystem = '{_subSystem}', @command = '{_command}', 
                                    @retry_attempts = 1, @retry_interval = 5;
                EXEC dbo.sp_add_jobserver @job_name = '{jobName}';
            ";

            databaseContext.QueryService.ExecuteNonProcessing(createJobQuery);

            Logger.Task($"Executing job {jobName}");
            string executeJobQuery = $"USE msdb; EXEC dbo.sp_start_job '{jobName}';";
            databaseContext.QueryService.ExecuteNonProcessing(executeJobQuery);

            Logger.Task("Deleting job...");
            string deleteJobQuery = $"USE msdb; EXEC dbo.sp_delete_job @job_name = '{jobName}';";
            databaseContext.QueryService.ExecuteNonProcessing(deleteJobQuery);

            return true;
        }

        /// <summary>
        /// Checks if the SQL Server Agent is running.
        /// </summary>
        private static bool AgentStatus(DatabaseContext databaseContext)
        {
            try
            {
                Logger.TaskNested("Checking SQL Server Agent status");

                string query = @"
                SELECT dss.[status], dss.[status_desc] 
                FROM sys.dm_server_services dss 
                WHERE dss.[servicename] LIKE 'SQL Server Agent (%';";

                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(result));

                if (result.Rows.Count == 0)
                {
                    Logger.Warning("No SQL Server Agent information found.");
                    return false;
                }

                foreach (DataRow row in result.Rows)
                {
                    int status = Convert.ToInt32(row["status"]);
                    string statusDesc = row["status_desc"]?.ToString()?.ToLower();

                    if (status == 4 && statusDesc == "running")
                    {
                        Logger.Success("SQL Server Agent is running.");
                        return true;
                    }
                }

                Logger.Warning("SQL Server Agent is not running.");
                return false;
            }
            catch (Exception ex)
            {
                if (ex.Message.ToLower().Contains("permission"))
                {
                    Logger.Error("The current user does not have permission to view Agent status.");
                }
                else
                {
                    Logger.Error($"Error checking SQL Server Agent status: {ex.Message}");
                }
                return false;
            }
        }
    }
}
