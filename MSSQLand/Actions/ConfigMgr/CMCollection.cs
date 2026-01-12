// MSSQLand/Actions/ConfigMgr/CMCollection.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.ConfigMgr
{
    /// <summary>
    /// Display comprehensive information about a specific ConfigMgr collection including all member devices.
    /// Shows collection details, membership rules, and complete list of devices/users in the collection.
    /// Use this to understand what devices are targeted by a specific collection.
    /// </summary>
    internal class CMCollection : BaseAction
    {
        [ArgumentMetadata(Position = 0, Description = "Collection ID to retrieve details for (e.g., SMS00001)")]
        private string _collectionId = "";

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            _collectionId = GetPositionalArgument(positional, 0, "")
                         ?? GetNamedArgument(named, "collection", null)
                         ?? GetNamedArgument(named, "c", null)
                         ?? "";

            if (string.IsNullOrWhiteSpace(_collectionId))
            {
                throw new ArgumentException("Collection ID is required");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Retrieving comprehensive collection information for: {_collectionId}");

            CMService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            var databases = sccmService.GetSccmDatabases();

            if (databases.Count == 0)
            {
                Logger.Warning("No ConfigMgr databases found");
                return null;
            }

            bool collectionFound = false;

            foreach (string db in databases)
            {
                string siteCode = CMService.GetSiteCode(db);

                Logger.NewLine();
                Logger.Info($"ConfigMgr database: {db} (Site Code: {siteCode})");

                // Get collection details
                string collectionQuery = $@"
SELECT 
    c.CollectionID,
    c.Name,
    c.Comment,
    c.CollectionType,
    CASE c.CollectionType
        WHEN 0 THEN 'Other'
        WHEN 1 THEN 'User'
        WHEN 2 THEN 'Device'
        ELSE 'Unknown'
    END AS TypeName,
    c.MemberCount,
    c.LastRefreshTime,
    c.LastMemberChangeTime,
    c.LastChangeTime,
    c.EvaluationStartTime,
    c.RefreshType,
    c.CurrentStatus,
    c.MemberClassName
FROM [{db}].dbo.v_Collection c
WHERE c.CollectionID = '{_collectionId.Replace("'", "''")}';";

                DataTable collectionResult = databaseContext.QueryService.ExecuteTable(collectionQuery);

                if (collectionResult.Rows.Count == 0)
                {
                    continue;
                }

                collectionFound = true;
                DataRow collection = collectionResult.Rows[0];

                // Display collection details
                Logger.NewLine();
                Logger.Info($"Collection: {collection["Name"]} ({_collectionId})");
                
                if (!string.IsNullOrEmpty(collection["Comment"].ToString()))
                {
                    Logger.InfoNested($"Description: {collection["Comment"]}");
                }
                
                Logger.InfoNested($"Type: {collection["TypeName"]} (CollectionType: {collection["CollectionType"]})");
                Logger.InfoNested($"Member Count: {collection["MemberCount"]}");
                Logger.InfoNested($"Refresh Type: {collection["RefreshType"]}");
                Logger.InfoNested($"Current Status: {collection["CurrentStatus"]}");
                Logger.InfoNested($"Last Refresh: {collection["LastRefreshTime"]}");
                Logger.InfoNested($"Last Member Change: {collection["LastMemberChangeTime"]}");
                Logger.InfoNested($"Evaluation Start: {collection["EvaluationStartTime"]}");

                Logger.NewLine();
                Logger.Info("Collection Properties");
                Console.WriteLine(OutputFormatter.ConvertDataTable(collectionResult));

                int memberCount = collection["MemberCount"] != DBNull.Value ? Convert.ToInt32(collection["MemberCount"]) : 0;
                int collectionType = Convert.ToInt32(collection["CollectionType"]);

                // Get deployments targeting this collection
                Logger.NewLine();
                Logger.Info("Deployments Targeting This Collection");

                string deploymentsQuery = $@"
SELECT 
    CASE 
        WHEN ds.AssignmentID = 0 THEN CAST(adv.AdvertisementID AS VARCHAR)
        ELSE CAST(ds.AssignmentID AS VARCHAR)
    END AS DeploymentID,
    ds.SoftwareName,
    ds.FeatureType,
    ds.DeploymentIntent,
    ds.NumberSuccess,
    ds.NumberInProgress,
    ds.NumberErrors,
    ds.NumberUnknown,
    ds.DeploymentTime,
    ds.ModificationTime
FROM [{db}].dbo.v_DeploymentSummary ds
LEFT JOIN [{db}].dbo.v_Advertisement adv ON ds.PackageID = adv.PackageID 
    AND adv.CollectionID = ds.CollectionID 
    AND ds.FeatureType = 2
WHERE ds.CollectionID = '{_collectionId.Replace("'", "''")}'
ORDER BY ds.DeploymentTime DESC;";

                DataTable deploymentsResult = databaseContext.QueryService.ExecuteTable(deploymentsQuery);
                
                if (deploymentsResult.Rows.Count > 0)
                {
                    // Add decoded FeatureType column
                    DataColumn decodedFeatureColumn = deploymentsResult.Columns.Add("DeploymentType", typeof(string));
                    int featureTypeIndex = deploymentsResult.Columns["FeatureType"].Ordinal;
                    decodedFeatureColumn.SetOrdinal(featureTypeIndex);

                    // Add decoded DeploymentIntent column
                    DataColumn decodedIntentColumn = deploymentsResult.Columns.Add("Intent", typeof(string));
                    int deploymentIntentIndex = deploymentsResult.Columns["DeploymentIntent"].Ordinal;
                    decodedIntentColumn.SetOrdinal(deploymentIntentIndex);

                    foreach (DataRow row in deploymentsResult.Rows)
                    {
                        row["DeploymentType"] = CMService.DecodeFeatureType(row["FeatureType"]);
                        row["Intent"] = CMService.DecodeDeploymentIntent(row["DeploymentIntent"]);
                    }

                    // Remove raw numeric columns
                    deploymentsResult.Columns.Remove("FeatureType");
                    deploymentsResult.Columns.Remove("DeploymentIntent");

                    Console.WriteLine(OutputFormatter.ConvertDataTable(deploymentsResult));
                    Logger.Success($"Found {deploymentsResult.Rows.Count} deployment(s) targeting this collection");
                }
                else
                {
                    Logger.Warning("No deployments targeting this collection");
                }

                // Get collection membership rules
                Logger.NewLine();
                Logger.Info("Collection Membership Rules");

                string rulesQuery = $@"
SELECT 
    cq.RuleName,
    'Query' AS RuleType,
    cq.QueryExpression,
    NULL AS ResourceID,
    cq.LimitToCollectionID
FROM [{db}].dbo.v_CollectionRuleQuery cq
WHERE cq.CollectionID = '{_collectionId.Replace("'", "''")}'
UNION ALL
SELECT 
    cd.RuleName,
    'Direct' AS RuleType,
    NULL AS QueryExpression,
    cd.ResourceID,
    NULL AS LimitToCollectionID
FROM [{db}].dbo.v_CollectionRuleDirect cd
WHERE cd.CollectionID = '{_collectionId.Replace("'", "''")}'
UNION ALL
SELECT 
    cr.QueryName AS RuleName,
    CASE cr.RuleType
        WHEN 3 THEN 'Include Collection'
        WHEN 4 THEN 'Exclude Collection'
        ELSE 'Other'
    END AS RuleType,
    NULL AS QueryExpression,
    NULL AS ResourceID,
    cr.ReferencedCollectionID AS LimitToCollectionID
FROM [{db}].dbo.Collection_Rules cr
WHERE cr.CollectionID = (SELECT CollectionID FROM [{db}].dbo.Collections_G WHERE SiteID = '{_collectionId.Replace("'", "''")}')
    AND cr.RuleType IN (3, 4)
ORDER BY RuleType, RuleName;";

                DataTable rulesResult = databaseContext.QueryService.ExecuteTable(rulesQuery);
                
                if (rulesResult.Rows.Count > 0)
                {
                    Console.WriteLine(OutputFormatter.ConvertDataTable(rulesResult));
                    Logger.Info($"Collection has {rulesResult.Rows.Count} membership rule(s)");
                }
                else
                {
                    Logger.Warning("No membership rules found");
                }

                // Get collection members (at end for better visibility)
                Logger.NewLine();
                Logger.Info($"Collection Members ({memberCount})");

                if (memberCount > 0)
                {
                    string membersQuery;
                    
                    if (collectionType == 2) // Device collection
                    {
                        membersQuery = $@"
SELECT 
    sys.Name0 AS DeviceName,
    sys.ResourceID,
    sys.Resource_Domain_OR_Workgr0 AS Domain,
    sys.User_Name0 AS LastUser,
    sys.Operating_System_Name_and0 AS OperatingSystem,
    sys.Client0 AS HasClient,
    sys.Client_Version0 AS ClientVersion,
    sys.AD_Site_Name0 AS ADSite,
    cm.IsDirect
FROM [{db}].dbo.v_FullCollectionMembership cm
INNER JOIN [{db}].dbo.v_R_System sys ON cm.ResourceID = sys.ResourceID
WHERE cm.CollectionID = '{_collectionId.Replace("'", "''")}'
ORDER BY sys.Client0 DESC, sys.Name0;";
                    }
                    else // User collection
                    {
                        membersQuery = $@"
SELECT 
    usr.Unique_User_Name0 AS UserName,
    usr.Full_User_Name0 AS FullName,
    usr.User_Principal_Name0 AS UPN,
    usr.Mail0 AS Email,
    usr.department0 AS Department,
    usr.title0 AS Title,
    cm.IsDirect
FROM [{db}].dbo.v_FullCollectionMembership cm
INNER JOIN [{db}].dbo.v_R_User usr ON cm.ResourceID = usr.ResourceID
WHERE cm.CollectionID = '{_collectionId.Replace("'", "''")}'
ORDER BY usr.Unique_User_Name0;";
                    }

                    DataTable membersResult = databaseContext.QueryService.ExecuteTable(membersQuery);
                    
                    if (membersResult.Rows.Count > 0)
                    {
                        Console.WriteLine(OutputFormatter.ConvertDataTable(membersResult));
                        Logger.Success($"Listed {membersResult.Rows.Count} member(s)");
                    }
                    else
                    {
                        Logger.Warning("No members found (member count may be stale)");
                    }
                }
                else
                {
                    Logger.Warning("Collection has no members");
                }

                break; // Found collection, no need to check other databases
            }

            if (!collectionFound)
            {
                Logger.Warning($"Collection with ID '{_collectionId}' not found in any ConfigMgr database");
            }

            return null;
        }
    }
}
