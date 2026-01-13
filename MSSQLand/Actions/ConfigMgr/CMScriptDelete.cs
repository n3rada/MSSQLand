// MSSQLand/Actions/ConfigMgr/CMScriptDelete.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using System;

namespace MSSQLand.Actions.ConfigMgr
{
    /// <summary>
    /// Remove a script from ConfigMgr's Scripts table by GUID to clean up after operations.
    /// Use this to delete scripts added via sccm-script-add, removing evidence of custom payloads.
    /// Requires script GUID - use sccm-scripts to find GUIDs.
    /// Automatically blocks deletion of built-in CMPivot script to maintain ConfigMgr functionality.
    /// Useful for operational security and cleaning up test scripts.
    /// </summary>
    internal class CMScriptDelete : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "g", LongName = "guid", Description = "Script GUID to delete", Required = true)]
        private string _scriptGuid;

        private const string BUILT_IN_CMPIVOT_GUID = "7DC6B6F1-E7F6-43C1-96E0-E1D16BC25C14";

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);

            // Block deletion of built-in CMPivot
            if (_scriptGuid.Equals(BUILT_IN_CMPIVOT_GUID, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Cannot delete the built-in CMPivot script");
            }
        }

        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Deleting ConfigMgr script: {_scriptGuid}");

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
