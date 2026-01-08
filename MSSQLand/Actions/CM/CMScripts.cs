using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.CM
{
    /// <summary>
    /// Enumerate PowerShell scripts stored in SCCM with metadata overview.
    /// Use 'sccm-script <GUID>' to view full details and script content for a specific script.
    /// Shows script names, GUIDs, approval status, authors, versions, and last update times.
    /// </summary>
    internal class CMScripts : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "n", LongName = "name", Description = "Filter by script name")]
        private string _name = "";

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            _name = GetNamedArgument(named, "n", null)
                 ?? GetNamedArgument(named, "name", null)
                 ?? GetPositionalArgument(positional, 0, "");
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            string filterMsg = !string.IsNullOrEmpty(_name) ? " (filtered)" : "";
            Logger.TaskNested($"Enumerating SCCM scripts{filterMsg}");

            CMService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            var databases = sccmService.GetSccmDatabases();

            if (databases.Count == 0)
            {
                Logger.Warning("No SCCM databases found");
                return null;
            }

            foreach (string db in databases)
            {
                string siteCode = CMService.GetSiteCode(db);

                Logger.NewLine();
                Logger.Info($"SCCM database: {db} (Site Code: {siteCode})");

                // Build WHERE clause - exclude CMPivot built-in script
                string whereClause = "WHERE ScriptGuid != '7DC6B6F1-E7F6-43C1-96E0-E1D16BC25C14'";
                
                if (!string.IsNullOrEmpty(_name))
                {
                    whereClause += $" AND ScriptName LIKE '%{_name.Replace("'", "''")}%'";
                }

                // Select all columns except Script blob
                string query = $@"
SELECT 
    ScriptGuid,
    ScriptVersion,
    ScriptName,
    Author,
    ScriptType,
    ApprovalState,
    Approver,
    ScriptHashAlgorithm,
    ScriptHash,
    LastUpdateTime,
    Comment,
    Error,
    Reserved,
    ParamsDefinition,
    Feature,
    MinBuildVersion,
    SEDOComponentID,
    ScriptDescription,
    Timeout
FROM [{db}].dbo.Scripts 
{whereClause} 
ORDER BY LastUpdateTime DESC;";

                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result.Rows.Count == 0)
                {
                    Logger.Warning("No scripts found");
                    continue;
                }

                Console.WriteLine(OutputFormatter.ConvertDataTable(result));
                
                Logger.Success($"Found {result.Rows.Count} script(s)");
                Logger.SuccessNested("Use 'sccm-script <GUID>' to view full content and details for a specific script");
            }

            return null;
        }
    }
}
