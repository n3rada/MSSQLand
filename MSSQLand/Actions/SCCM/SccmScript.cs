using System;
using System.Data;
using System.Text;
using MSSQLand.Services;
using MSSQLand.Utilities;

namespace MSSQLand.Actions.SCCM
{
    /// <summary>
    /// Display detailed information for a specific SCCM PowerShell script including full content and parameters.
    /// </summary>
    internal class SccmScript : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Script GUID to retrieve")]
        private string _scriptGuid = "";

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            _scriptGuid = GetPositionalArgument(positional, 0, "")
                       ?? GetNamedArgument(named, "g", null)
                       ?? GetNamedArgument(named, "guid", null)
                       ?? "";

            if (string.IsNullOrWhiteSpace(_scriptGuid))
            {
                throw new ArgumentException("Script GUID is required");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Retrieving SCCM script: {_scriptGuid}");

            SccmService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            var databases = sccmService.GetSccmDatabases();

            if (databases.Count == 0)
            {
                Logger.Warning("No SCCM databases found");
                return null;
            }

            bool foundScript = false;

            foreach (string db in databases)
            {
                string siteCode = SccmService.GetSiteCode(db);

                string query = $@"SELECT * FROM [{db}].dbo.Scripts WHERE ScriptGuid = '{_scriptGuid.Replace("'", "''")}';";

                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result.Rows.Count == 0)
                {
                    continue;
                }

                foundScript = true;
                DataRow row = result.Rows[0];

                Logger.NewLine();
                Logger.Info($"SCCM database: {db} (Site Code: {siteCode})");

                // Display script metadata
                string scriptName = row.Field<string>("ScriptName") ?? "";
                string scriptGuid = row.Field<Guid?>("ScriptGuid")?.ToString() ?? "";
                string author = row.Field<string>("Author") ?? "";
                string approver = row.Field<string>("Approver") ?? "";
                int approvalState = row.Field<int?>("ApprovalState") ?? 0;
                string scriptVersion = row.Field<string>("ScriptVersion") ?? "";
                DateTime lastUpdate = row.Field<DateTime?>("LastUpdateTime") ?? DateTime.MinValue;
                string description = row.Field<string>("ScriptDescription") ?? "";

                Logger.NewLine();
                Logger.Info($"{scriptName} ({scriptGuid})");
                
                if (!string.IsNullOrEmpty(description))
                {
                    Logger.InfoNested($"Description: {description}");
                }
                
                Logger.InfoNested($"Author: {author}");
                Logger.InfoNested($"Version: {scriptVersion}");
                
                string approvalStateStr = approvalState switch
                {
                    0 => "Waiting",
                    1 => "Declined",
                    3 => "Approved",
                    _ => approvalState.ToString()
                };
                Logger.InfoNested($"Approval State: {approvalStateStr}");
                
                if (!string.IsNullOrEmpty(approver))
                {
                    Logger.InfoNested($"Approver: {approver}");
                }
                
                if (lastUpdate != DateTime.MinValue)
                {
                    Logger.InfoNested($"Last Updated: {lastUpdate:yyyy-MM-dd HH:mm:ss}");
                }

                // Display script parameters if available
                string scriptParamsDefinition = row.Field<string>("ParamsDefinition") ?? "";
                if (!string.IsNullOrWhiteSpace(scriptParamsDefinition))
                {
                    Logger.NewLine();
                    Logger.Info("Script Parameters:");
                    try
                    {
                        string decodedParams = Encoding.ASCII.GetString(Convert.FromBase64String(scriptParamsDefinition));
                        Console.WriteLine(decodedParams);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to decode parameters: {ex.Message}");
                    }
                }

                // Display script content
                Logger.NewLine();
                Logger.Info("Script Content:");
                Logger.NewLine();

                byte[] scriptBlob = row.Field<byte[]>("Script") ?? Array.Empty<byte>();
                if (scriptBlob.Length > 0)
                {
                    var (encoding, bomLength) = Misc.DetectEncoding(scriptBlob);
                    string decodedScript = Misc.DecodeText(scriptBlob, encoding, bomLength);
                    Console.WriteLine(decodedScript);
                }
                else
                {
                    Logger.Warning("Script content is empty");
                }

                break; // Found the script, no need to check other databases
            }

            if (!foundScript)
            {
                Logger.Warning($"Script with GUID '{_scriptGuid}' not found in any SCCM database");
            }

            return null;
        }
    }
}
