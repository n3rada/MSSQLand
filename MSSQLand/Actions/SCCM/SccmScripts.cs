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
    internal class SccmScripts : BaseAction
    {
        public override void ValidateArguments(string[] args)
        {
            // No arguments required
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Enumerating SCCM scripts");

            SccmService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            string[] requiredTables = { "Scripts" };
            var databases = sccmService.GetValidatedSccmDatabases(requiredTables, 1);

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

                string query = $"SELECT * FROM [{db}].dbo.Scripts WHERE ScriptName <> 'CMPivot' ORDER BY LastUpdateTime DESC;";

                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result.Rows.Count == 0)
                {
                    Logger.Warning("No scripts found");
                    continue;
                }

                foreach (DataRow row in result.Rows)
                {
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

                    byte[] scriptBlob = row.Field<byte[]>("Script") ?? Array.Empty<byte>();
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
