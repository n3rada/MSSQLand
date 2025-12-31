using MSSQLand.Services;
using MSSQLand.Utilities;
using System;

namespace MSSQLand.Actions.SCCM
{
    /// <summary>
    /// Delete a script from the SCCM Scripts table by GUID.
    /// Blocks deletion of the built-in CMPivot script (7DC6B6F1-E7F6-43C1-96E0-E1D16BC25C14).
    /// </summary>
    internal class SccmScriptDelete : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "g", LongName = "guid", Description = "Script GUID to delete")]
        private string _scriptGuid;

        private const string BUILT_IN_CMPIVOT_GUID = "7DC6B6F1-E7F6-43C1-96E0-E1D16BC25C14";

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            // Named arguments
            if (named.TryGetValue("g", out string guid) || named.TryGetValue("guid", out guid))
            {
                _scriptGuid = guid;
            }

            // Positional arguments
            if (!named.ContainsKey("g") && !named.ContainsKey("guid") && positional.Count > 0)
            {
                _scriptGuid = positional[0];
            }

            if (string.IsNullOrWhiteSpace(_scriptGuid))
            {
                throw new ArgumentException("Script GUID is required (--guid or -g)");
            }

            // Block deletion of built-in CMPivot
            if (_scriptGuid.Equals(BUILT_IN_CMPIVOT_GUID, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Cannot delete the built-in CMPivot script");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Deleting SCCM script: {_scriptGuid}");

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
                Logger.Info($"SCCM database: {db} (Site Code: {siteCode})");

                try
                {
                    string deleteQuery = $@"
DELETE FROM [{db}].dbo.Scripts 
WHERE ScriptGuid = '{_scriptGuid}'";

                    int rowsAffected = databaseContext.QueryService.ExecuteNonProcessing(deleteQuery);

                    if (rowsAffected > 0)
                    {
                        Logger.Success($"Script deleted successfully ({rowsAffected} row(s) affected)");
                    }
                    else
                    {
                        Logger.Warning($"No script found with GUID: {_scriptGuid}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to delete script: {ex.Message}");
                }
            }

            return null;
        }
    }
}
