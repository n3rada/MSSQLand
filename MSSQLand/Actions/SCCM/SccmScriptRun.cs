using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;
using System.Text;
using System.Threading;

namespace MSSQLand.Actions.SCCM
{
    /// <summary>
    /// Execute a PowerShell script on a target device via SCCM's BGB (Background) notification channel.
    /// 
    /// Workflow:
    /// 1. Create BGB_Task entry (TemplateID=15 for script execution)
    /// 2. Create BGB_ResTask entry to assign task to ResourceID
    /// 3. Monitor ScriptsExecutionStatus for output
    /// 
    /// Requires:
    /// - Script must exist in Scripts table (use sccm-script-add)
    /// - Target device must be online with BGB channel active
    /// </summary>
    internal class SccmScriptRun : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "r", LongName = "resourceid", Description = "Target device ResourceID")]
        private string _resourceId;

        [ArgumentMetadata(Position = 1, ShortName = "g", LongName = "scriptguid", Description = "Script GUID to execute")]
        private string _scriptGuid;

        [ArgumentMetadata(Position = 2, ShortName = "w", LongName = "wait", Description = "Wait for execution output (default: true)")]
        private bool _waitForOutput = true;

        [ArgumentMetadata(Position = 3, ShortName = "t", LongName = "timeout", Description = "Timeout in seconds (default: 60)")]
        private int _timeout = 60;

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            // Named arguments
            if (named.TryGetValue("r", out string resourceId) || named.TryGetValue("resourceid", out resourceId))
            {
                _resourceId = resourceId;
            }

            if (named.TryGetValue("g", out string guid) || named.TryGetValue("scriptguid", out guid))
            {
                _scriptGuid = guid;
            }

            if (named.TryGetValue("w", out string wait) || named.TryGetValue("wait", out wait))
            {
                _waitForOutput = bool.Parse(wait);
            }

            if (named.TryGetValue("t", out string timeout) || named.TryGetValue("timeout", out timeout))
            {
                _timeout = int.Parse(timeout);
            }

            // Positional arguments
            if (!named.ContainsKey("r") && !named.ContainsKey("resourceid") && positional.Count > 0)
            {
                _resourceId = positional[0];
            }

            if (!named.ContainsKey("g") && !named.ContainsKey("scriptguid") && positional.Count > 1)
            {
                _scriptGuid = positional[1];
            }

            if (string.IsNullOrWhiteSpace(_resourceId))
            {
                throw new ArgumentException("ResourceID is required (--resourceid or -r)");
            }

            if (string.IsNullOrWhiteSpace(_scriptGuid))
            {
                throw new ArgumentException("Script GUID is required (--scriptguid or -g)");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Executing SCCM script on ResourceID: {_resourceId}");

            SccmService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            string[] requiredTables = { "Scripts", "BGB_Task", "BGB_ResTask", "ScriptsExecutionStatus" };
            var databases = sccmService.GetValidatedSccmDatabases(requiredTables, 4);

            if (databases.Count == 0)
            {
                Logger.Warning("No SCCM databases found");
                return null;
            }

            foreach (string db in databases)
            {
                string siteCode = SccmService.GetSiteCode(db);
                Logger.Info($"SCCM database: {db} (Site Code: {siteCode})");

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

                    // Step 7: Wait for execution output
                    if (_waitForOutput)
                    {
                        Logger.TaskNested($"Waiting for execution output (timeout: {_timeout}s)");
                        
                        int elapsed = 0;
                        int pollInterval = 5;
                        bool outputReceived = false;

                        while (elapsed < _timeout)
                        {
                            Thread.Sleep(pollInterval * 1000);
                            elapsed += pollInterval;

                            string outputQuery = $@"
SELECT ScriptExecutionState, ScriptExitCode, ScriptOutput 
FROM [{db}].dbo.ScriptsExecutionStatus 
WHERE TaskID = {taskId}";

                            DataTable outputResult = databaseContext.QueryService.ExecuteTable(outputQuery);

                            if (outputResult.Rows.Count > 0)
                            {
                                outputReceived = true;
                                int exitCode = Convert.ToInt32(outputResult.Rows[0]["ScriptExitCode"]);
                                string scriptOutput = outputResult.Rows[0]["ScriptOutput"]?.ToString() ?? string.Empty;

                                Logger.NewLine();
                                Logger.Success($"Script execution completed (Exit Code: {exitCode})");
                                Logger.TaskNested("Script Output:");
                                Logger.NewLine();

                                if (!string.IsNullOrEmpty(scriptOutput))
                                {
                                    try
                                    {
                                        // Try to parse as JSON array
                                        var lines = System.Text.Json.JsonSerializer.Deserialize<string[]>(scriptOutput);
                                        foreach (var line in lines)
                                        {
                                            Console.WriteLine(line);
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

                                break;
                            }

                            Logger.InfoNested($"Waiting... ({elapsed}s / {_timeout}s)");
                        }

                        if (!outputReceived)
                        {
                            Logger.Warning("Timeout waiting for output - script may still be executing");
                            Logger.InfoNested($"Check manually: SELECT * FROM ScriptsExecutionStatus WHERE TaskID = {taskId}");
                        }
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
