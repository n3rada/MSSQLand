// MSSQLand/Actions/Execution/Agents.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
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

        [ArgumentMetadata(Position = 0, Description = "Action mode: status or exec (default: status)")]
        private ActionMode _action = ActionMode.Status;
        
        [ArgumentMetadata(Position = 1, Description = "Command to execute (required for exec mode)")]
        private string _command = null;
        
        [ArgumentMetadata(Position = 2, Description = "Subsystem: cmd, powershell, tsql, vbscript (default: powershell)")]
        private SubSystemMode _subSystem = SubSystemMode.PowerShell;

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);

            // Additional validation
            if (_action == ActionMode.Exec && string.IsNullOrEmpty(_command))
            {
                throw new ArgumentException("Missing command to execute. Example: agents exec 'whoami'");
            }
        }

        public override object Execute(DatabaseContext databaseContext)
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
        private object ListAgentJobs(DatabaseContext databaseContext)
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

            Logger.Success($"Found {jobsTable.Rows.Count} SQL Agent job(s)");
            Console.WriteLine(OutputFormatter.ConvertDataTable(jobsTable));

            return jobsTable;
        }

        /// <summary>
        /// Executes a command using SQL Server Agent.
        /// </summary>
        private object ExecuteAgentJob(DatabaseContext databaseContext)
        {
            if (!AgentStatus(databaseContext))
            {
                return null;
            }

            Logger.Info($"Creating and executing agent job with {_subSystem} subsystem...");

            string jobName = $"AZ_Job_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            string stepName = $"AZ_Step_{Guid.NewGuid().ToString("N").Substring(0, 8)}";

            try
            {
                // Create job
                string createJobQuery = $@"
                    EXEC msdb.dbo.sp_add_job 
                        @job_name = '{jobName}', 
                        @enabled = 1, 
                        @description = 'MSSQLand temporary job';";

                databaseContext.QueryService.ExecuteNonProcessing(createJobQuery);
                Logger.Success($"Job '{jobName}' created");

                // Add job step
                string addStepQuery = $@"
                    EXEC msdb.dbo.sp_add_jobstep 
                        @job_name = '{jobName}',
                        @step_name = '{stepName}',
                        @subsystem = '{_subSystem}',
                        @command = '{_command.Replace("'", "''")}', // Escape single quotes in command
                        @retry_attempts = 0,
                        @retry_interval = 0;";

                databaseContext.QueryService.ExecuteNonProcessing(addStepQuery);
                Logger.Success($"Job step '{stepName}' added with {_subSystem} subsystem");

                // Add job server
                string addServerQuery = $"EXEC msdb.dbo.sp_add_jobserver @job_name = '{jobName}', @server_name = '(local)';";
                databaseContext.QueryService.ExecuteNonProcessing(addServerQuery);

                // Start job
                Logger.Info($"Starting job '{jobName}'...");
                string startJobQuery = $"EXEC msdb.dbo.sp_start_job @job_name = '{jobName}';";
                databaseContext.QueryService.ExecuteNonProcessing(startJobQuery);

                Logger.Success($"Job '{jobName}' started successfully");
                Logger.Warning("Note: This is an asynchronous execution. Check job history for output.");

                // Clean up
                System.Threading.Thread.Sleep(2000); // Wait 2 seconds for job to execute
                
                Logger.Info($"Cleaning up job '{jobName}'...");
                string deleteJobQuery = $"EXEC msdb.dbo.sp_delete_job @job_name = '{jobName}';";
                databaseContext.QueryService.ExecuteNonProcessing(deleteJobQuery);
                Logger.Success("Job cleaned up");

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to execute agent job: {ex.Message}");
                
                // Try to clean up
                try
                {
                    string deleteJobQuery = $"EXEC msdb.dbo.sp_delete_job @job_name = '{jobName}';";
                    databaseContext.QueryService.ExecuteNonProcessing(deleteJobQuery);
                }
                catch
                {
                    // Ignore cleanup errors
                }

                return false;
            }
        }

        /// <summary>
        /// Checks if SQL Server Agent is running.
        /// </summary>
        private bool AgentStatus(DatabaseContext databaseContext)
        {
            try
            {
                string query = @"
                    IF EXISTS (SELECT 1 FROM master.dbo.sysprocesses WHERE program_name LIKE 'SQLAgent%')
                        SELECT 'Running' AS AgentStatus
                    ELSE
                        SELECT 'Stopped' AS AgentStatus;";

                DataTable result = databaseContext.QueryService.ExecuteTable(query);
                string status = result.Rows[0]["AgentStatus"].ToString();

                if (status == "Running")
                {
                    Logger.Success("SQL Server Agent is running");
                    return true;
                }
                else
                {
                    Logger.Error("SQL Server Agent is not running");
                    Logger.Info("Agent jobs require the SQL Server Agent service to be active.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to check Agent status: {ex.Message}");
                return false;
            }
        }
    }
}
