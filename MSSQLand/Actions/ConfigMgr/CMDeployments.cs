using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.ConfigMgr
{
    /// <summary>
    /// Enumerate ConfigMgr deployments showing what content is being pushed to which collections.
    /// Use this to identify active deployments for hijacking or monitoring deployed content.
    /// Shows deployment names, target collections, deployment types (Available/Required), schedules, and content IDs.
    /// Reveals which devices will receive which packages/applications, enabling deployment poisoning attacks.
    /// Filter by deployment name, collection, type, or intent to find specific campaigns.
    /// </summary>
    internal class CMDeployments : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "n", LongName = "name", Description = "Filter by software/deployment name")]
        private string _name = "";

        [ArgumentMetadata(Position = 1, ShortName = "c", LongName = "collection", Description = "Filter by collection name or ID")]
        private string _collection = "";

        [ArgumentMetadata(Position = 2, ShortName = "t", LongName = "type", Description = "Filter by feature type: application (1), program (2), script (4), task-sequence (7)")]
        private string _featureType = "";

        [ArgumentMetadata(Position = 3, ShortName = "i", LongName = "intent", Description = "Filter by intent: required (1), available (2)")]
        private string _intent = "";

        [ArgumentMetadata(Position = 4, LongName = "with-errors", Description = "Show only deployments with errors")]
        private bool _withErrors = false;

        [ArgumentMetadata(Position = 5, LongName = "in-progress", Description = "Show only deployments in progress")]
        private bool _inProgress = false;

        [ArgumentMetadata(Position = 6, LongName = "limit", Description = "Limit number of results (default: 50)")]
        private int _limit = 50;

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            _name = GetNamedArgument(named, "n", null)
                 ?? GetNamedArgument(named, "name", null)
                 ?? GetPositionalArgument(positional, 0, "");

            _collection = GetNamedArgument(named, "c", null)
                       ?? GetNamedArgument(named, "collection", null)
                       ?? GetPositionalArgument(positional, 1, "");

            _featureType = GetNamedArgument(named, "t", null)
                        ?? GetNamedArgument(named, "type", null)
                        ?? GetPositionalArgument(positional, 2, "");

            _intent = GetNamedArgument(named, "i", null)
                   ?? GetNamedArgument(named, "intent", null)
                   ?? GetPositionalArgument(positional, 3, "");

            _withErrors = named.ContainsKey("with-errors");
            _inProgress = named.ContainsKey("in-progress");

            string limitStr = GetNamedArgument(named, "l", null)
                           ?? GetNamedArgument(named, "limit", null)
                           ?? GetPositionalArgument(positional, 5);
            if (!string.IsNullOrEmpty(limitStr))
            {
                _limit = int.Parse(limitStr);
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            string filterMsg = "";
            if (!string.IsNullOrEmpty(_name))
                filterMsg += $" name: {_name}";
            if (!string.IsNullOrEmpty(_collection))
                filterMsg += $" collection: {_collection}";
            if (!string.IsNullOrEmpty(_featureType))
                filterMsg += $" type: {_featureType}";
            if (!string.IsNullOrEmpty(_intent))
                filterMsg += $" intent: {_intent}";
            if (_withErrors)
                filterMsg += " with-errors";
            if (_inProgress)
                filterMsg += " in-progress";

            Logger.TaskNested($"Enumerating ConfigMgr deployments{(string.IsNullOrEmpty(filterMsg) ? "" : $" (filter:{filterMsg})")}");
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

                string filterClause = "WHERE 1=1";
                
                if (!string.IsNullOrEmpty(_name))
                {
                    filterClause += $" AND ds.SoftwareName LIKE '%{_name.Replace("'", "''")}%'";
                }

                if (!string.IsNullOrEmpty(_collection))
                {
                    filterClause += $" AND (c.Name LIKE '%{_collection.Replace("'", "''")}%' OR ds.CollectionID LIKE '%{_collection.Replace("'", "''")}%')";
                }

                if (!string.IsNullOrEmpty(_featureType))
                {
                    int featureTypeValue = _featureType.ToLower() switch
                    {
                        "application" or "app" => 1,
                        "program" or "package" => 2,
                        "script" => 4,
                        "task-sequence" or "tasksequence" or "ts" => 7,
                        _ => int.TryParse(_featureType, out int val) ? val : -1
                    };
                    
                    if (featureTypeValue != -1)
                    {
                        filterClause += $" AND ds.FeatureType = {featureTypeValue}";
                    }
                }

                if (!string.IsNullOrEmpty(_intent))
                {
                    int intentValue = _intent.ToLower() switch
                    {
                        "required" => 1,
                        "available" => 2,
                        _ => int.TryParse(_intent, out int val) ? val : -1
                    };
                    
                    if (intentValue != -1)
                    {
                        filterClause += $" AND ds.DeploymentIntent = {intentValue}";
                    }
                }

                if (_withErrors)
                {
                    filterClause += " AND ds.NumberErrors > 0";
                }

                if (_inProgress)
                {
                    filterClause += " AND ds.NumberInProgress > 0";
                }

                string query = $@"
SELECT TOP {_limit}
    CASE 
        WHEN ds.FeatureType = 2 THEN adv.AdvertisementID
        ELSE CAST(ds.AssignmentID AS VARCHAR)
    END AS DeploymentID,
    CASE 
        WHEN ds.FeatureType = 2 THEN 'Advertisement'
        ELSE 'Assignment'
    END AS DeploymentKind,
    ds.SoftwareName,
    ds.CollectionID,
    c.Name AS CollectionName,
    CASE ds.FeatureType
        WHEN 1 THEN 'Application'
        WHEN 2 THEN 'Program'
        WHEN 3 THEN 'Mobile Program'
        WHEN 4 THEN 'Script'
        WHEN 5 THEN 'Software Update'
        WHEN 6 THEN 'Baseline'
        WHEN 7 THEN 'Task Sequence'
        WHEN 8 THEN 'Content Distribution'
        WHEN 9 THEN 'Distribution Point Group'
        WHEN 10 THEN 'Distribution Point Health'
        WHEN 11 THEN 'Configuration Policy'
        ELSE CAST(ds.FeatureType AS VARCHAR)
    END AS FeatureType,
    CASE ds.DeploymentIntent
        WHEN 1 THEN 'Required'
        WHEN 2 THEN 'Available'
        WHEN 3 THEN 'Simulate'
        ELSE CAST(ds.DeploymentIntent AS VARCHAR)
    END AS DeploymentIntent,
    ds.NumberTotal,
    ds.NumberSuccess,
    ds.NumberInProgress,
    ds.NumberErrors,
    ds.NumberOther,
    ds.NumberUnknown,
    ds.Enabled,
    ds.DeploymentTime,
    ds.CreationTime,
    ds.ModificationTime,
    ds.SummarizationTime,
    dt.CI_UniqueID AS DeploymentTypeGUID,
    COALESCE(lp.DisplayName, dt.CI_UniqueID) AS DeploymentTypeName
FROM [{db}].dbo.v_DeploymentSummary ds
LEFT JOIN [{db}].dbo.v_Collection c ON ds.CollectionID = c.CollectionID
LEFT JOIN [{db}].dbo.vAppDeploymentTargetingInfoBase adt ON ds.AssignmentID = adt.AssignmentID
LEFT JOIN [{db}].dbo.CI_ConfigurationItems dt ON adt.DTCI = dt.CI_ID
LEFT JOIN [{db}].dbo.v_LocalizedCIProperties lp ON dt.CI_ID = lp.CI_ID AND lp.LocaleID = 1033
LEFT JOIN [{db}].dbo.v_Advertisement adv ON ds.CollectionID = adv.CollectionID 
    AND ds.PackageID = adv.PackageID 
    AND ds.ProgramName = adv.ProgramName
    AND ds.FeatureType = 2
{filterClause}
ORDER BY ds.CreationTime DESC;
";

                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result.Rows.Count == 0)
                {
                    Logger.Warning("No deployments found");
                    continue;
                }

                Console.WriteLine(OutputFormatter.ConvertDataTable(result));

                Logger.Success($"Found {result.Rows.Count} deployment(s)");
            }

            return null;
        }
    }
}
