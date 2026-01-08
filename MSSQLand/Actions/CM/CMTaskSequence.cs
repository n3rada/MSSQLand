using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.CM
{
    /// <summary>
    /// Display detailed information about a specific ConfigMgr Task Sequence including all referenced content.
    /// 
    /// PackageID uniquely identifies a task sequence (1:1 relationship). Task sequences are packages 
    /// and each has a unique PackageID (e.g., PSC00001) that serves as the primary key.
    /// 
    /// Shows packages, drivers, applications, OS images, and boot images used in the task sequence.
    /// Use this to analyze what content is deployed by a specific task sequence.
    /// </summary>
    internal class CMTaskSequence : BaseAction
    {
        [ArgumentMetadata(Position = 0, Description = "Task Sequence PackageID (e.g., PSC002C0)")]
        private string _packageId = "";

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            _packageId = GetPositionalArgument(positional, 0, "");

            if (string.IsNullOrEmpty(_packageId))
            {
                throw new ArgumentException("Task Sequence PackageID is required");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Retrieving task sequence details for: {_packageId}");

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

                // Get task sequence details
                string tsQuery = $@"
SELECT 
    ts.PackageID,
    ts.Name,
    ts.Description,
    ts.Version,
    ts.Manufacturer,
    ts.Language,
    ts.SourceDate,
    ts.SourceVersion,
    ts.PkgSourcePath AS SourcePath,
    ts.StoredPkgPath,
    ts.LastRefreshTime,
    ts.BootImageID,
    bi.Name AS BootImageName,
    ts.TS_Type,
    ts.TS_Flags,
    (
        SELECT COUNT(*) 
        FROM [{db}].dbo.v_TaskSequenceReferencesInfo ref 
        WHERE ref.PackageID = ts.PackageID
    ) AS ReferencedContentCount
FROM [{db}].dbo.v_TaskSequencePackage ts
LEFT JOIN [{db}].dbo.v_BootImagePackage bi ON ts.BootImageID = bi.PackageID
WHERE ts.PackageID = '{_packageId.Replace("'", "''")}'
";

                DataTable tsResult = databaseContext.QueryService.ExecuteTable(tsQuery);

                if (tsResult.Rows.Count == 0)
                {
                    Logger.Warning($"Task sequence '{_packageId}' not found");
                    continue;
                }

                DataRow tsRow = tsResult.Rows[0];
                string name = tsRow["Name"].ToString();
                string description = tsRow["Description"].ToString();
                int refCount = tsRow["ReferencedContentCount"] != DBNull.Value ? Convert.ToInt32(tsRow["ReferencedContentCount"]) : 0;

                Logger.NewLine();
                Logger.Success($"Task Sequence: {name} ({_packageId})");
                if (!string.IsNullOrEmpty(description))
                {
                    Logger.Info($"Description: {description}");
                }
                Logger.Info($"Referenced Content Count: {refCount}");

                Logger.NewLine();
                Logger.Info("Task Sequence Properties:");
                Console.WriteLine(OutputFormatter.ConvertDataTable(tsResult));

                // Get referenced content
                if (refCount > 0)
                {
                    Logger.NewLine();
                    Logger.Info($"Referenced Content ({refCount} item(s)):");

                    string refQuery = $@"
SELECT 
    ref.ReferencePackageID,
    CASE ref.ReferencePackageType
        WHEN 0 THEN 'Package'
        WHEN 3 THEN 'Driver Package'
        WHEN 5 THEN 'Software Update Package'
        WHEN 257 THEN 'Operating System Image'
        WHEN 258 THEN 'Boot Image'
        WHEN 259 THEN 'Operating System Installer'
        WHEN 512 THEN 'Application'
        ELSE 'Unknown (' + CAST(ref.ReferencePackageType AS VARCHAR) + ')'
    END AS ContentType,
    ref.ReferenceName AS ContentName,
    ref.ReferenceVersion AS Version,
    ref.ReferenceDescription AS Description,
    ref.ReferenceProgramName AS ProgramName
FROM [{db}].dbo.v_TaskSequenceReferencesInfo ref
WHERE ref.PackageID = '{_packageId.Replace("'", "''")}'
ORDER BY ref.ReferencePackageType, ref.ReferenceName;
";

                    DataTable refResult = databaseContext.QueryService.ExecuteTable(refQuery);
                    Console.WriteLine(OutputFormatter.ConvertDataTable(refResult));

                    // Summary by content type
                    Logger.NewLine();
                    Logger.Info("Content Summary by Type:");
                    string summaryQuery = $@"
SELECT 
    CASE ref.ReferencePackageType
        WHEN 0 THEN 'Package'
        WHEN 3 THEN 'Driver Package'
        WHEN 5 THEN 'Software Update Package'
        WHEN 257 THEN 'Operating System Image'
        WHEN 258 THEN 'Boot Image'
        WHEN 259 THEN 'Operating System Installer'
        WHEN 512 THEN 'Application'
        ELSE 'Unknown'
    END AS ContentType,
    COUNT(*) AS Count
FROM [{db}].dbo.v_TaskSequenceReferencesInfo ref
WHERE ref.PackageID = '{_packageId.Replace("'", "''")}'
GROUP BY ref.ReferencePackageType
ORDER BY COUNT(*) DESC;
";

                    DataTable summaryResult = databaseContext.QueryService.ExecuteTable(summaryQuery);
                    Console.WriteLine(OutputFormatter.ConvertDataTable(summaryResult));
                }
                else
                {
                    Logger.Warning("No referenced content found");
                }

                // Get deployments/advertisements for this task sequence
                Logger.NewLine();
                Logger.Info("Task Sequence Deployments:");

                string deploymentsQuery = $@"
SELECT 
    adv.AdvertisementID,
    adv.AdvertisementName,
    adv.CollectionID,
    c.Name AS CollectionName,
    c.MemberCount,
    adv.PresentTime,
    adv.ExpirationTime,
    CASE 
        WHEN adv.AdvertFlags & 0x00000020 = 0x00000020 THEN 'Required'
        ELSE 'Available'
    END AS DeploymentType,
    CASE 
        WHEN adv.AdvertFlags & 0x00000400 = 0x00000400 THEN 'Yes'
        ELSE 'No'
    END AS AllowUsersToRunIndependently,
    CASE 
        WHEN adv.AdvertFlags & 0x00008000 = 0x00008000 THEN 'Yes'
        ELSE 'No'
    END AS RerunBehavior
FROM [{db}].dbo.v_Advertisement adv
LEFT JOIN [{db}].dbo.v_Collection c ON adv.CollectionID = c.CollectionID
WHERE adv.PackageID = '{_packageId.Replace("'", "''")}'
ORDER BY adv.PresentTime DESC;";

                DataTable deploymentsResult = databaseContext.QueryService.ExecuteTable(deploymentsQuery);
                
                if (deploymentsResult.Rows.Count > 0)
                {
                    Console.WriteLine(OutputFormatter.ConvertDataTable(deploymentsResult));
                    Logger.Success($"Task sequence deployed to {deploymentsResult.Rows.Count} collection(s)");
                    
                    // Show total potential reach
                    int totalMembers = 0;
                    foreach (DataRow row in deploymentsResult.Rows)
                    {
                        if (row["MemberCount"] != DBNull.Value)
                            totalMembers += Convert.ToInt32(row["MemberCount"]);
                    }
                    Logger.Info($"Total devices potentially targeted: {totalMembers}");
                    Logger.InfoNested("Use 'cm-collection <CollectionID>' to see which devices are in each collection");
                }
                else
                {
                    Logger.Warning("Task sequence not deployed to any collections");
                }

                // Get deployment status summary
                Logger.NewLine();
                Logger.Info("Deployment Status Summary:");

                string statusQuery = $@"
SELECT 
    ds.SoftwareName,
    ds.CollectionID,
    c.Name AS CollectionName,
    CASE ds.DeploymentIntent
        WHEN 1 THEN 'Required'
        WHEN 2 THEN 'Available'
        WHEN 3 THEN 'Simulate'
        ELSE CAST(ds.DeploymentIntent AS VARCHAR)
    END AS Intent,
    ds.NumberSuccess,
    ds.NumberInProgress,
    ds.NumberErrors,
    ds.NumberUnknown,
    ds.DeploymentTime,
    ds.ModificationTime
FROM [{db}].dbo.v_DeploymentSummary ds
LEFT JOIN [{db}].dbo.v_Collection c ON ds.CollectionID = c.CollectionID
WHERE ds.PackageID = '{_packageId.Replace("'", "''")}'
    AND ds.FeatureType = 7
ORDER BY ds.DeploymentTime DESC;";

                DataTable statusResult = databaseContext.QueryService.ExecuteTable(statusQuery);
                
                if (statusResult.Rows.Count > 0)
                {
                    Console.WriteLine(OutputFormatter.ConvertDataTable(statusResult));
                }
                else
                {
                    Logger.Info("No deployment status information available");
                }

                break; // Found task sequence, no need to check other databases
            }

            return null;
        }
    }
}
