// MSSQLand/Actions/ConfigMgr/CMApplications.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.ConfigMgr
{
    /// <summary>
    /// Enumerate ConfigMgr applications with deployment types, installation commands, and detection methods.
    /// Use this to view application inventory including DisplayName, ModelName, deployment status, and content paths.
    /// Applications are the modern deployment model (since ConfigMgr 2012) with detection rules, dependencies, and supersedence.
    /// For legacy package deployments, use sccm-packages instead.
    /// 
    /// Note: Install/uninstall command lines are stored in the SDMPackageXML column (XML format).
    /// Query v_ConfigurationItems.SDMPackageXML to extract deployment type command lines and detection methods.
    /// </summary>
    internal class CMApplications : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "n", LongName = "displayname", Description = "Filter by DisplayName")]
        private string _displayName = "";

        [ArgumentMetadata(Position = 1, ShortName = "m", LongName = "modelname", Description = "Filter by ModelName")]
        private string _modelName = "";

        [ArgumentMetadata(Position = 2, LongName = "limit", Description = "Limit number of results (default: 25)")]
        private int _limit = 25;

        public override object Execute(DatabaseContext databaseContext)
        {
            string filterMsg = "";
            if (!string.IsNullOrEmpty(_displayName))
                filterMsg += $" displayname: {_displayName}";
            if (!string.IsNullOrEmpty(_modelName))
                filterMsg += $" modelname: {_modelName}";
            
            Logger.TaskNested($"Enumerating ConfigMgr applications{(string.IsNullOrEmpty(filterMsg) ? "" : $" (filter:{filterMsg})")}");
            Logger.TaskNested($"Limit: {_limit}");

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

                Logger.NewLine();
                Logger.Info($"ConfigMgr database: {db} (Site Code: {siteCode})");

                string filterClause = "WHERE ci.CIType_ID = 10";
                
                if (!string.IsNullOrEmpty(_displayName))
                {
                    filterClause += $" AND lp.DisplayName LIKE '%{_displayName.Replace("'", "''")}%'";
                }
                
                if (!string.IsNullOrEmpty(_modelName))
                {
                    filterClause += $" AND ci.ModelName LIKE '%{_modelName.Replace("'", "''")}%'";
                }

                string query = $@"
SELECT TOP {_limit}
    ci.CI_ID,
    COALESCE(lp.DisplayName, ci.ModelName) AS DisplayName,
    ci.ModelName,
    ci.CI_UniqueID,
    ci.CIVersion,
    ci.IsDeployed,
    ci.IsEnabled,
    ci.IsExpired,
    ci.IsSuperseded,
    ci.IsHidden,
    ci.ContentSourcePath,
    ci.CreatedBy,
    ci.DateCreated,
    ci.LastModifiedBy,
    ci.DateLastModified
FROM [{db}].dbo.v_ConfigurationItems ci
LEFT JOIN (
    SELECT CI_ID, MIN(DisplayName) AS DisplayName
    FROM [{db}].dbo.v_LocalizedCIProperties
    WHERE DisplayName IS NOT NULL AND DisplayName != ''
    GROUP BY CI_ID
) lp ON ci.CI_ID = lp.CI_ID
{filterClause}
ORDER BY ci.IsDeployed DESC, ci.DateCreated DESC;
";

                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result.Rows.Count == 0)
                {
                    Logger.Warning("No applications found");
                    continue;
                }

                Console.WriteLine(OutputFormatter.ConvertDataTable(result));

                Logger.Success($"Found {result.Rows.Count} application(s)");
            }

            Logger.Success("Application enumeration completed");
            return null;
        }
    }
}
