using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;

namespace MSSQLand.Actions.CM
{
    /// <summary>
    /// Monitor execution status and retrieve output from scripts run via sccm-script-run.
    /// Use this to check if script execution completed, failed, or is still pending on target devices.
    /// Shows task state (Pending/Success/Failed), execution time, script output, and error messages.
    /// Requires Task ID returned by sccm-script-run command.
    /// Polls ScriptsExecutionStatus table which updates when client reports back to ConfigMgr.
    /// Essential for confirming command execution and retrieving command output from remote devices.
    /// </summary>
    internal class CMScriptStatus : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "t", LongName = "taskid", Description = "Task ID to check", Required = true)]
        private string _taskId;

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            // TaskID argument (required)
            _taskId = GetNamedArgument(named, "t", null)
                   ?? GetNamedArgument(named, "taskid", null)
                   ?? GetPositionalArgument(positional, 0);

            if (string.IsNullOrWhiteSpace(_taskId))
            {
                throw new ArgumentException("Task ID is required (--taskid or -t)");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Checking status for Task ID: {_taskId}");

            CMService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            var databases = sccmService.GetSccmDatabases();

            if (databases.Count == 0)
            {
                Logger.Warning("No ConfigMgr databases found");
                return null;
            }

            foreach (string db in databases)
            {
                string siteCode = CMService.GetSiteCode(db);
                Logger.Info($"ConfigMgr database: {db} (Site Code: {siteCode})");

                try
                {
                    string statusQuery = $@"
SELECT 
    ses.TaskID,
    ses.ScriptExecutionState,
    ses.ScriptExitCode,
    ses.ScriptOutput,
    ses.LastUpdateTime,
    s.ScriptName,
    s.ScriptGuid,
    sys.Name0 AS DeviceName,
    sys.ResourceID
FROM [{db}].dbo.ScriptsExecutionStatus ses
LEFT JOIN [{db}].dbo.Scripts s ON ses.ScriptGuid = s.ScriptGuid
LEFT JOIN [{db}].dbo.v_R_System sys ON ses.ResourceID = sys.ResourceID
WHERE ses.TaskID = {_taskId}";

                    DataTable statusResult = databaseContext.QueryService.ExecuteTable(statusQuery);

                    if (statusResult.Rows.Count == 0)
                    {
                        Logger.Warning($"No execution record found for Task ID: {_taskId}");
                        Logger.InfoNested("Task may still be in queue or device is offline");
                        continue;
                    }

                    var row = statusResult.Rows[0];
                    string scriptName = row["ScriptName"]?.ToString() ?? "Unknown";
                    string scriptGuid = row["ScriptGuid"]?.ToString() ?? "Unknown";
                    string deviceName = row["DeviceName"]?.ToString() ?? "Unknown";
                    int resourceId = row["ResourceID"] != DBNull.Value ? Convert.ToInt32(row["ResourceID"]) : 0;
                    string executionState = row["ScriptExecutionState"]?.ToString() ?? "Unknown";
                    int exitCode = row["ScriptExitCode"] != DBNull.Value ? Convert.ToInt32(row["ScriptExitCode"]) : -1;
                    string scriptOutput = row["ScriptOutput"]?.ToString() ?? string.Empty;
                    DateTime? lastUpdate = row["LastUpdateTime"] != DBNull.Value ? Convert.ToDateTime(row["LastUpdateTime"]) : null;

                    Logger.NewLine();
                    Logger.Success($"Task Status Found");
                    Logger.InfoNested($"Script: {scriptName} ({scriptGuid})");
                    Logger.InfoNested($"Device: {deviceName} (ResourceID: {resourceId})");
                    Logger.InfoNested($"Execution State: {executionState}");
                    Logger.InfoNested($"Exit Code: {exitCode}");
                    if (lastUpdate.HasValue)
                    {
                        Logger.InfoNested($"Last Update: {lastUpdate.Value:yyyy-MM-dd HH:mm:ss}");
                    }

                    if (!string.IsNullOrEmpty(scriptOutput))
                    {
                        Logger.NewLine();
                        Logger.TaskNested("Script Output");
                        Logger.NewLine();

                        try
                        {
                            // Try to parse as JSON array (simple parsing for .NET 4.8)
                            if (scriptOutput.StartsWith("[") && scriptOutput.EndsWith("]"))
                            {
                                string cleaned = scriptOutput.Trim('[', ']').Replace("\\\\", "\\").Replace("\\\"", "\"");
                                string[] lines = cleaned.Split(new[] { "\",\"" }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var line in lines)
                                {
                                    Console.WriteLine(line.Trim('\"'));
                                }
                            }
                            else
                            {
                                Console.WriteLine(scriptOutput);
                            }
                        }
                        catch
                        {
                            // Raw output
                            Console.WriteLine(scriptOutput);
                        }
                    }
                    else
                    {
                        Logger.NewLine();
                        Logger.Warning("No output available yet");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to check status: {ex.Message}");
                }
            }

            return null;
        }
    }
}
