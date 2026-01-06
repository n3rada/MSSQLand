using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace MSSQLand.Actions.SCCM
{
    /// <summary>
    /// Enumerate PowerShell scripts stored in SCCM's Scripts table with metadata and content.
    /// Use this to view existing scripts, extract their content, or identify script GUIDs for execution.
    /// Shows script names, GUIDs, approval status, authors, last update times, and script parameters.
    /// Can display full script content for specific GUIDs or filter by name.
    /// Essential for identifying existing administrative scripts to execute or finding script GUIDs for sccm-script-run.
    /// </summary>
    internal class SccmScripts : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "f", LongName = "filter", Description = "Filter by script name or GUID")]
        private string _filter = "";

        [ArgumentMetadata(Position = 1, ShortName = "g", LongName = "guid", Description = "Show specific script by GUID")]
        private string _guid = "";

        [ArgumentMetadata(Position = 2, ShortName = "c", LongName = "content", Description = "Show only script content (no metadata)")]
        private bool _contentOnly = false;

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            _filter = GetNamedArgument(named, "f", null)
                   ?? GetNamedArgument(named, "filter", null)
                   ?? GetPositionalArgument(positional, 0, "");

            _guid = GetNamedArgument(named, "g", null)
                 ?? GetNamedArgument(named, "guid", null);

            string contentStr = GetNamedArgument(named, "c", null)
                             ?? GetNamedArgument(named, "content", null);
            if (!string.IsNullOrEmpty(contentStr))
            {
                _contentOnly = bool.Parse(contentStr);
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            string filterMsg = !string.IsNullOrEmpty(_filter) || !string.IsNullOrEmpty(_guid) ? " (filtered)" : "";
            Logger.TaskNested($"Enumerating SCCM scripts{filterMsg}");

            SccmService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            var databases = sccmService.GetSccmDatabases();

            if (databases.Count == 0)
            {
                Logger.Warning("No SCCM databases found");
                return null;
            }

            foreach (string db in databases)
            {
                string siteCode = SccmService.GetSiteCode(db);

                Logger.NewLine();
                Logger.Info($"SCCM database: {db} (Site Code: {siteCode})");

                // Build WHERE clause
                string whereClause = "WHERE ScriptGuid != '7DC6B6F1-E7F6-43C1-96E0-E1D16BC25C14'"; // Exclude built-in CMPivot
                
                if (!string.IsNullOrEmpty(_guid))
                {
                    whereClause += $" AND ScriptGuid = '{_guid.Replace("'", "''")}'";
                }
                else if (!string.IsNullOrEmpty(_filter))
                {
                    whereClause += $" AND (ScriptName LIKE '%{_filter.Replace("'", "''")}%' OR ScriptGuid LIKE '%{_filter.Replace("'", "''")}%')";
                }

                string query = $"SELECT * FROM [{db}].dbo.Scripts {whereClause} ORDER BY LastUpdateTime DESC;";

                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result.Rows.Count == 0)
                {
                    Logger.Warning("No scripts found");
                    continue;
                }

                foreach (DataRow row in result.Rows)
                {
                    byte[] scriptBlob = row.Field<byte[]>("Script") ?? Array.Empty<byte>();
                    
                    if (_contentOnly)
                    {
                        // Content-only mode: just output the script
                        if (scriptBlob.Length != 0)
                        {
                            var (encoding, bomLength) = Misc.DetectEncoding(scriptBlob);
                            string decodedScript = Misc.DecodeText(scriptBlob, encoding, bomLength);
                            Console.Write(decodedScript);
                        }
                        continue;
                    }

                    // Normal mode: show metadata + content
                    string scriptName = row.Field<string>("ScriptName") ?? string.Empty;

                    string scriptGUID = row.Field<Guid?>("ScriptGuid")?.ToString() ?? string.Empty;

                    string author = row.Field<string>("Author") ?? string.Empty;

                    Logger.NewLine();
                    Logger.Info($"{scriptName} (GUID: {scriptGUID}) - {author}");

                    DateTime lastUpdated = row.Field<DateTime?>("LastUpdateTime") ?? DateTime.MinValue;

                    if (lastUpdated != DateTime.MinValue)
                    {
                        Logger.InfoNested($"Last Updated: {lastUpdated}");
                    }

                    string scriptDescription = row.Field<string>("ScriptDescription") ?? string.Empty;

                    if (!string.IsNullOrEmpty(scriptDescription))
                    {
                        Logger.InfoNested($"Description: {scriptDescription}");
                    }

                    string scriptParamsDefinition = row.Field<string>("ParamsDefinition") ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(scriptParamsDefinition))
                    {
                        string decodedParams = Encoding.ASCII.GetString(Convert.FromBase64String(scriptParamsDefinition));
                        Logger.InfoNested($"Script Parameters: {decodedParams}");
                    }

                    Logger.InfoNested("Script Content:");
                    Logger.NewLine();

                    if (scriptBlob.Length != 0)
                    {
                        var (encoding, bomLength) = Misc.DetectEncoding(scriptBlob);
                        string decodedScript = Misc.DecodeText(scriptBlob, encoding, bomLength);
                        Console.WriteLine(decodedScript);
                    }
                }
            }

            return null;
        }
    }
}
