// MSSQLand/Actions/ConfigMgr/CMPackage.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.ConfigMgr
{
    /// <summary>
    /// Display comprehensive information about a specific ConfigMgr package including programs and deployments.
    /// PackageID uniquely identifies a package (1:1 relationship).
    /// Shows package details, all programs with command lines and execution flags, and deployment information.
    /// Use this to analyze what a specific package does and where it's deployed.
    /// </summary>
    internal class CMPackage : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Package PackageID to retrieve details for (e.g., PSC004BF)")]
        private string _packageId = "";

        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Retrieving comprehensive package information for: {_packageId}");

            CMService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            var databases = sccmService.GetSccmDatabases();

            if (databases.Count == 0)
            {
                Logger.Warning("No ConfigMgr databases found");
                return null;
            }

            bool packageFound = false;

            foreach (string db in databases)
            {
                string siteCode = CMService.GetSiteCode(db);

                Logger.NewLine();
                Logger.Info($"ConfigMgr database: {db} (Site Code: {siteCode})");

                // Get package details
                string packageQuery = $@"
SELECT 
    p.PackageID,
    p.Name,
    p.Description,
    p.PkgSourcePath,
    p.Manufacturer,
    p.Version,
    p.Language,
    p.PackageType,
    p.StoredPkgPath,
    p.SourceVersion,
    p.SourceDate,
    p.LastRefreshTime,
    p.Priority,
    p.PkgFlags,
    p.PreferredAddressType,
    (SELECT COUNT(*) FROM [{db}].dbo.v_Program pr WHERE pr.PackageID = p.PackageID) AS ProgramCount
FROM [{db}].dbo.v_Package p
WHERE p.PackageID = '{_packageId.Replace("'", "''")}'";

                DataTable packageResult = databaseContext.QueryService.ExecuteTable(packageQuery);

                if (packageResult.Rows.Count == 0)
                {
                    continue;
                }

                packageFound = true;
                DataRow package = packageResult.Rows[0];

                // Check if this is a task sequence - redirect to cm-tasksequence
                int packageTypeValue = package["PackageType"] != DBNull.Value ? Convert.ToInt32(package["PackageType"]) : 0;
                if (packageTypeValue == 4)
                {
                    Logger.NewLine();
                    Logger.Warning($"Package {_packageId} is a Task Sequence");
                    Logger.WarningNested($"Task sequences have complex step-by-step workflows that require special parsing");
                    Logger.WarningNested($"Use 'cm-tasksequence {_packageId}' to see detailed task sequence information");
                }

                // Display package details
                Logger.NewLine();
                Logger.Info($"Package: {package["Name"]} ({_packageId})");
                
                // Description: NULL = no description provided
                if (package["Description"] != DBNull.Value && !string.IsNullOrEmpty(package["Description"].ToString()))
                {
                    Logger.InfoNested($"Description: {package["Description"]}");
                }
                
                // Manufacturer/Version: NULL = not specified
                string manufacturer = package["Manufacturer"] != DBNull.Value ? package["Manufacturer"].ToString() : "(Not specified)";
                string version = package["Version"] != DBNull.Value ? package["Version"].ToString() : "(Not specified)";
                Logger.InfoNested($"Manufacturer: {manufacturer} | Version: {version}");
                
                // Language: NULL = language-neutral or not specified
                string language = package["Language"] != DBNull.Value ? package["Language"].ToString() : "(Not specified)";
                Logger.InfoNested($"Language: {language}");
                
                // PackageType: NULL = standard package (0)
                string packageType = CMService.DecodePackageType(package["PackageType"]);
                Logger.InfoNested($"Package Type: {packageType}");
                
                // Source Path: NULL = no source files (virtual package or legacy)
                string sourcePath = package["PkgSourcePath"] != DBNull.Value ? package["PkgSourcePath"].ToString() : "(No source path)";
                Logger.InfoNested($"Source Path: {sourcePath}");
                
                // Stored Package Path: NULL = not stored on distribution points yet
                string storedPath = package["StoredPkgPath"] != DBNull.Value ? package["StoredPkgPath"].ToString() : "(Not stored)";
                Logger.InfoNested($"Stored Package Path: {storedPath}");
                
                // Source Version/Date: NULL = never refreshed from source
                string sourceVersion = package["SourceVersion"] != DBNull.Value ? package["SourceVersion"].ToString() : "(Not available)";
                string sourceDate = package["SourceDate"] != DBNull.Value ? Convert.ToDateTime(package["SourceDate"]).ToString("yyyy-MM-dd HH:mm:ss") : "(Not available)";
                Logger.InfoNested($"Source Version: {sourceVersion} | Source Date: {sourceDate}");
                
                // Last Refresh: NULL = never refreshed
                string lastRefresh = package["LastRefreshTime"] != DBNull.Value ? Convert.ToDateTime(package["LastRefreshTime"]).ToString("yyyy-MM-dd HH:mm:ss") : "(Never refreshed)";
                Logger.InfoNested($"Last Refresh: {lastRefresh}");
                
                // Priority: NULL = default priority (2 - Normal)
                string priority = package["Priority"] != DBNull.Value ? package["Priority"].ToString() : "2 (Normal)";
                Logger.InfoNested($"Priority: {priority}");

                Logger.NewLine();
                Logger.Info("Package Properties");
                Console.WriteLine(OutputFormatter.ConvertDataTable(packageResult));

                // Get programs for this package
                int programCount = package["ProgramCount"] != DBNull.Value ? Convert.ToInt32(package["ProgramCount"]) : 0;
                
                if (programCount > 0)
                {
                    
                    Logger.Info($"Programs ({programCount})");

                    string programsQuery = $@"
SELECT 
    pr.ProgramName,
    pr.CommandLine,
    pr.WorkingDirectory,
    pr.Comment,
    pr.ProgramFlags,
    pr.Duration,
    pr.DiskSpaceRequired,
    pr.Requirements,
    pr.DependentProgram,
    CASE 
        WHEN pr.ProgramFlags & 0x00000001 = 0x00000001 THEN 'Yes'
        ELSE 'No'
    END AS AuthorizedDynamicInstall,
    CASE 
        WHEN pr.ProgramFlags & 0x00001000 = 0x00001000 THEN 'Disabled'
        ELSE 'Enabled'
    END AS Status
FROM [{db}].dbo.v_Program pr
WHERE pr.PackageID = '{_packageId.Replace("'", "''")}'
ORDER BY pr.ProgramName;";

                    DataTable programsResult = databaseContext.QueryService.ExecuteTable(programsQuery);
                    
                    // Add decoded ProgramFlags column before ProgramFlags
                    DataColumn decodedFlagsColumn = programsResult.Columns.Add("DecodedFlags", typeof(string));
                    int programFlagsIndex = programsResult.Columns["ProgramFlags"].Ordinal;
                    decodedFlagsColumn.SetOrdinal(programFlagsIndex);

                    foreach (DataRow row in programsResult.Rows)
                    {
                        if (row["ProgramFlags"] != DBNull.Value)
                        {
                            int signedFlags = Convert.ToInt32(row["ProgramFlags"]);
                            uint flags = unchecked((uint)signedFlags);
                            row["DecodedFlags"] = CMService.DecodeProgramFlags(flags);
                        }
                    }
                    
                    Console.WriteLine(OutputFormatter.ConvertDataTable(programsResult));
                }

                // Get advertisements/deployments for this package
                string advertisementsQuery = $@"
SELECT 
    adv.AdvertisementID,
    adv.AdvertisementName,
    adv.CollectionID,
    c.Name AS CollectionName,
    adv.ProgramName,
    adv.PresentTime,
    adv.ExpirationTime,
    adv.AdvertFlags,
    CASE 
        WHEN adv.AdvertFlags & 0x00000020 = 0x00000020 THEN 'Required'
        ELSE 'Available'
    END AS DeploymentType
FROM [{db}].dbo.v_Advertisement adv
LEFT JOIN [{db}].dbo.v_Collection c ON adv.CollectionID = c.CollectionID
WHERE adv.PackageID = '{_packageId.Replace("'", "''")}'
ORDER BY adv.PresentTime DESC;";

                DataTable advertisementsResult = databaseContext.QueryService.ExecuteTable(advertisementsQuery);
                
                if (advertisementsResult.Rows.Count > 0)
                {
                    Logger.NewLine();
                    Logger.Info("Advertisements/Deployments");
                    
                    // Add decoded AdvertFlags column before AdvertFlags
                    DataColumn decodedAdvertColumn = advertisementsResult.Columns.Add("DecodedFlags", typeof(string));
                    int advertFlagsIndex = advertisementsResult.Columns["AdvertFlags"].Ordinal;
                    decodedAdvertColumn.SetOrdinal(advertFlagsIndex);

                    foreach (DataRow row in advertisementsResult.Rows)
                    {
                        row["DecodedFlags"] = CMService.DecodeAdvertFlags(row["AdvertFlags"]);
                    }

                    Console.WriteLine(OutputFormatter.ConvertDataTable(advertisementsResult));
                    Logger.Success($"Found {advertisementsResult.Rows.Count} advertisement(s)/deployment(s)");

                    // Get targeted collections summary
                    Logger.NewLine();
                    Logger.Info("Targeted Collections Summary");

                    string targetedCollectionsQuery = $@"
SELECT DISTINCT
    c.CollectionID,
    c.Name AS CollectionName,
    c.MemberCount,
    adv.AdvertisementName,
    adv.ProgramName,
    CASE 
        WHEN adv.AdvertFlags & 0x00000020 = 0x00000020 THEN 'Required'
        ELSE 'Available'
    END AS DeploymentType
FROM [{db}].dbo.v_Advertisement adv
INNER JOIN [{db}].dbo.v_Collection c ON adv.CollectionID = c.CollectionID
WHERE adv.PackageID = '{_packageId.Replace("'", "''")}'
ORDER BY c.MemberCount DESC, c.Name;";

                    DataTable targetedCollectionsResult = databaseContext.QueryService.ExecuteTable(targetedCollectionsQuery);
                    
                    if (targetedCollectionsResult.Rows.Count > 0)
                    {
                        Console.WriteLine(OutputFormatter.ConvertDataTable(targetedCollectionsResult));
                        
                        int totalMemberships = 0;
                        foreach (DataRow row in targetedCollectionsResult.Rows)
                        {
                            if (row["MemberCount"] != DBNull.Value)
                                totalMemberships += Convert.ToInt32(row["MemberCount"]);
                        }
                        
                        Logger.Success($"Package is deployed to {targetedCollectionsResult.Rows.Count} collection(s) with {totalMemberships} total membership(s)");
                        Logger.SuccessNested($"Note: Same device may appear in multiple collections");
                        Logger.SuccessNested($"Use 'sccm-collection <CollectionID>' to see device members in each collection");
                    }
                }

                // Get package distribution status summary
                Logger.NewLine();
                string statusQuery = $@"
SELECT 
    psd.SiteCode,
    psd.SiteName,
    psd.SourceVersion,
    psd.Targeted,
    psd.Installed,
    psd.Retrying,
    psd.Failed,
    psd.SummaryDate
FROM [{db}].dbo.v_PackageStatusDetailSumm psd
WHERE psd.PackageID = '{_packageId.Replace("'", "''")}'
ORDER BY psd.SiteCode;";

                DataTable statusResult = databaseContext.QueryService.ExecuteTable(statusQuery);
                
                if (statusResult.Rows.Count > 0)
                {
                    Logger.Info("Package Distribution Status");
                    Console.WriteLine(OutputFormatter.ConvertDataTable(statusResult));
                    Logger.Info($"Distribution status across {statusResult.Rows.Count} site(s)");
                }

                // Get distribution points where this package is distributed
                Logger.NewLine();

                string distributionQuery = $@"
SELECT *
FROM [{db}].dbo.v_DistributionPoint dp
WHERE dp.PackageID = '{_packageId.Replace("'", "''")}'
ORDER BY dp.ServerNALPath;";

                DataTable distributionResult = databaseContext.QueryService.ExecuteTable(distributionQuery);
                
                if (distributionResult.Rows.Count > 0)
                {
                    Logger.Info("Distribution Points");
                    Console.WriteLine(OutputFormatter.ConvertDataTable(distributionResult));
                    Logger.Success($"Package distributed to {distributionResult.Rows.Count} distribution point(s)");
                }
                else
                {
                    Logger.Warning("Package not distributed to any distribution points");
                }

                break; // Found package, no need to check other databases
            }

            if (!packageFound)
            {
                Logger.Warning($"Package with PackageID '{_packageId}' not found in any ConfigMgr database");
            }

            return null;
        }
    }
}
