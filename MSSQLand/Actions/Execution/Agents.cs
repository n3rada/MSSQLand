using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;


namespace MSSQLand.Actions.Execution
{
    internal class Agents : BaseAction
    {
        public string _action = "status";
        public string _command = null;
        public string _subSystem = "PowerShell";

        public override void ValidateArguments(string additionalArguments)
        {

            // Split the additional argument into parts (dll URI and function)
            string[] parts = SplitArguments(additionalArguments);

            if (parts.Length == 0)
            {
                return;
            }

            _action = parts[0].Trim().ToLower();

            // Validate action
            if (_action != "status" && _action != "exec")
            {
                throw new ArgumentException($"Invalid action: {_action}. Valid actions are: status, exec.");
            }

            if (parts.Length == 2)
            {
                _command = parts[1].Trim();
            }

            // Optional subsystem argument
            if (parts.Length > 2)
            {
                _subSystem = parts[2].Trim();
            }

        }


        public override void Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested(_action);


            if (_action == "status")
            {
                if (AgentStatus(databaseContext) == true)
                {
                    string agentJobsQuery = "SELECT job_id, name, enabled, date_created, date_modified FROM msdb.dbo.sysjobs ORDER BY date_created;";

                    string queryResultMardkownTable = MarkdownFormatter.ConvertDataTableToMarkdownTable(databaseContext.QueryService.ExecuteTable(agentJobsQuery));

                    if (queryResultMardkownTable.ToLower().Contains("job_id"))
                    {
                        Logger.Info($"Agent Jobs:\n{queryResultMardkownTable}");
                    }
                    else if (queryResultMardkownTable.ToLower().Contains("permission"))
                    {
                        Logger.Warning($"The current user does not have permissions to view agent information");
                    }
                    else
                    {
                        Logger.Info($"There are no jobs");
                    }
                }
                return;
            }

            if (_action == "exec")
            {
                if (string.IsNullOrEmpty(_command))
                {
                    Logger.Warning("No command provided to execute");
                    return;
                }

                Logger.TaskNested($"Executing: {_command}");
                Logger.TaskNested($"Using subsystem: {_subSystem}");

                if (AgentStatus(databaseContext) == false) { return; }

                string jobName = Guid.NewGuid().ToString("N").Substring(0, 6);
                string stepName = Guid.NewGuid().ToString("N").Substring(0, 6);

                Logger.Info($"Job name: {jobName}");
                Logger.Info($"Step name: {stepName}");


                string createJobQuery = $"use msdb;EXEC dbo.sp_add_job @job_name = '{jobName}';EXEC sp_add_jobstep @job_name = '{jobName}', @step_name = '{stepName}', @subsystem = '{_subSystem}', @command = '{_command}', @retry_attempts = 1, @retry_interval = 5;EXEC dbo.sp_add_jobserver @job_name = '{jobName}';";

                databaseContext.QueryService.ExecuteNonProcessing(createJobQuery);

                string agentJobsQuery = "SELECT job_id, name, enabled, date_created, date_modified FROM msdb.dbo.sysjobs ORDER BY date_created;";

                string queryResultMardkownTable = MarkdownFormatter.ConvertDataTableToMarkdownTable(databaseContext.QueryService.ExecuteTable(agentJobsQuery));

                Logger.Info($"Agent Jobs");
                Console.WriteLine(queryResultMardkownTable);

                if (queryResultMardkownTable.ToLower().Contains("job_id"))
                {

                    if (queryResultMardkownTable.ToLower().Contains(jobName.ToLower()))
                    {
                        Logger.Task($"Executing {jobName} and waiting for 3 seconds");

                        string executeJobQuery = $"use msdb; EXEC dbo.sp_start_job '{jobName}'; WAITFOR DELAY '00:00:03';";

                        databaseContext.QueryService.ExecuteNonProcessing(executeJobQuery);

                        Logger.Task("Deleting job");

                        string deleteJobQuery = $"use msdb; EXEC dbo.sp_delete_job  @job_name = '{jobName}';";
                        databaseContext.QueryService.ExecuteNonProcessing(deleteJobQuery);

                        Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(databaseContext.QueryService.ExecuteTable(agentJobsQuery)));
                    }
                }
                else if (queryResultMardkownTable.ToLower().Contains("permission"))
                {
                    Logger.Warning($"The current user does not have permissions to create new jobs");
                }
                else
                {
                    Logger.Info($"Unable to create new job '{jobName}'");
                }

                return;
            }

        }




        /// <summary>
        /// Checks whether the SQL Server Agent is running on the specified server.
        /// </summary>
        /// <param name="databaseContext">The database context to execute queries.</param>
        /// <returns>True if the SQL Server Agent is running, otherwise false.</returns>
        private static bool AgentStatus(DatabaseContext databaseContext)
        {
            try
            {
                Logger.TaskNested("Checking Agent status");
                string agentStatusQuery = @"
            SELECT dss.[status], dss.[status_desc] 
            FROM sys.dm_server_services dss 
            WHERE dss.[servicename] LIKE 'SQL Server Agent (%';";

                // Execute the query
                var dataTable = databaseContext.QueryService.ExecuteTable(agentStatusQuery);

                Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(dataTable));

                if (dataTable.Rows.Count == 0)
                {
                    Logger.Warning($"No SQL Server Agent information found");
                    return false;
                }

                foreach (DataRow row in dataTable.Rows)
                {
                    string statusDesc = row["status_desc"]?.ToString()?.ToLower();

                    if (statusDesc == "running")
                    {
                        Logger.Success($"SQL Server Agent is running");
                        return true;
                    }
                }

                Logger.Warning($"SQL Server Agent is not running");
                return false;
            }
            catch (Exception ex)
            {
                if (ex.Message.ToLower().Contains("permission"))
                {
                    Logger.Error($"The current user does not have permissions to view agent information");
                }
                else
                {
                    Logger.Error($"Error while checking SQL Server Agent status: {ex.Message}");
                }

                return false;
            }
        }

    }
}
