// MSSQLand/Actions/ConfigMgr/CMPackages.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.ConfigMgr
{
    /// <summary>
    /// Enumerate ConfigMgr packages with their properties, source locations, and program details.
    /// Use this to view package inventory including names, descriptions, source paths, versions, and associated program counts.
    /// Shows PackageID, name, source path (UNC or local), manufacturer, version, package type, and program count.
    /// Packages are the legacy deployment model - use cm-apps for modern application deployments.
    /// </summary>
    internal class CMPackages : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "n", LongName = "name", Description = "Filter by package name")]
        private string _name = "";

        [ArgumentMetadata(Position = 1, ShortName = "s", LongName = "source", Description = "Filter by package source path")]
        private string _sourcePath = "";

        [ArgumentMetadata(Position = 2, ShortName = "m", LongName = "manufacturer", Description = "Filter by manufacturer")]
        private string _manufacturer = "";

        [ArgumentMetadata(Position = 3, ShortName = "v", LongName = "version", Description = "Filter by version")]
        private string _version = "";

        [ArgumentMetadata(Position = 4, ShortName = "t", LongName = "type", Description = "Filter by package type: package (0), driver (3), task-sequence (4), software-update (5), os-image (257), boot-image (258)")]
        private string _packageType = "";

        [ArgumentMetadata(Position = 5, LongName = "with-programs", Description = "Show only packages with programs (default: false)")]
        private bool _withPrograms = false;

        [ArgumentMetadata(Position = 6, LongName = "with-deployments", Description = "Show only packages with active deployments (default: false)")]
        private bool _withDeployments = false;

        [ArgumentMetadata(Position = 7, LongName = "no-source", Description = "Show only packages without source path (virtual packages) (default: false)")]
        private bool _noSource = false;

        [ArgumentMetadata(Position = 8, LongName = "limit", Description = "Limit number of results (default: 25)")]
        private int _limit = 25;

        public override object Execute(DatabaseContext databaseContext)
        {
            string filterMsg = "";
            if (!string.IsNullOrEmpty(_name))
                filterMsg += $" name: {_name}";
            if (!string.IsNullOrEmpty(_sourcePath))
                filterMsg += $" source: {_sourcePath}";
            if (!string.IsNullOrEmpty(_manufacturer))
                filterMsg += $" manufacturer: {_manufacturer}";
            if (!string.IsNullOrEmpty(_version))
                filterMsg += $" version: {_version}";
            if (!string.IsNullOrEmpty(_packageType))
                filterMsg += $" type: {_packageType}";
            if (_withPrograms)
                filterMsg += " with-programs";
            if (_withDeployments)
                filterMsg += " with-deployments";
            if (_noSource)
                filterMsg += " no-source";

            Logger.TaskNested($"Enumerating ConfigMgr packages{(string.IsNullOrEmpty(filterMsg) ? "" : $" (filter:{filterMsg})")}");
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

                string whereClause = "WHERE 1=1";

                if (!string.IsNullOrEmpty(_name))
                {
                    whereClause += $" AND p.Name LIKE '%{_name.Replace("'", "''")}%'";
                }

                if (!string.IsNullOrEmpty(_sourcePath))
                {
                    whereClause += $" AND p.PkgSourcePath LIKE '%{_sourcePath.Replace("'", "''")}%'";
                }

                if (!string.IsNullOrEmpty(_manufacturer))
                {
                    whereClause += $" AND p.Manufacturer LIKE '%{_manufacturer.Replace("'", "''")}%'";
                }

                if (!string.IsNullOrEmpty(_version))
                {
                    whereClause += $" AND p.Version LIKE '%{_version.Replace("'", "''")}%'";
                }

                if (!string.IsNullOrEmpty(_packageType))
                {
                    int packageTypeValue = _packageType.ToLower() switch
                    {
                        "package" => 0,
                        "driver" or "driver-package" => 3,
                        "task-sequence" or "tasksequence" or "ts" => 4,
                        "software-update" or "update" => 5,
                        "os-image" or "image" => 257,
                        "boot-image" or "boot" => 258,
                        "os-installer" or "os-upgrade" => 259,
                        _ => int.TryParse(_packageType, out int val) ? val : -1
                    };

                    if (packageTypeValue != -1)
                    {
                        whereClause += $" AND p.PackageType = {packageTypeValue}";
                    }
                }

                if (_noSource)
                {
                    whereClause += " AND (p.PkgSourcePath IS NULL OR p.PkgSourcePath = '')";
                }

                string havingClause = "";
                if (_withPrograms)
                {
                    havingClause = " HAVING COUNT(DISTINCT pr.ProgramName) > 0";
                }
                if (_withDeployments)
                {
                    havingClause += (string.IsNullOrEmpty(havingClause) ? " HAVING" : " AND") + " COUNT(DISTINCT adv.AdvertisementID) > 0";
                }

                string query = $@"
SELECT TOP {_limit}
    p.PackageID,
    p.Name,
    p.Description,
    p.PkgSourcePath,
    p.Manufacturer,
    p.Version,
    p.PackageType AS PackageTypeRaw,
    p.StoredPkgPath,
    p.SourceVersion,
    p.SourceDate,
    p.LastRefreshTime,
    COUNT(DISTINCT pr.ProgramName) AS ProgramCount,
    COUNT(DISTINCT adv.AdvertisementID) AS DeploymentCount,
    MAX(CASE WHEN adv.AdvertFlags & 0x00000020 = 0x00000020 THEN 1 ELSE 0 END) AS HasRequired,
    MAX(CASE WHEN adv.AdvertFlags & 0x00400000 = 0x00400000 THEN 1 ELSE 0 END) AS HasWakeOnLAN,
    MAX(CASE WHEN adv.AdvertFlags & 0x00100000 = 0x00100000 THEN 1 ELSE 0 END) AS OverrideServiceWindows
FROM [{db}].dbo.v_Package p
LEFT JOIN [{db}].dbo.v_Program pr ON p.PackageID = pr.PackageID
LEFT JOIN [{db}].dbo.v_Advertisement adv ON p.PackageID = adv.PackageID
{whereClause}
GROUP BY 
    p.PackageID, p.Name, p.Description, p.PkgSourcePath, 
    p.Manufacturer, p.Version, p.PackageType,
    p.StoredPkgPath, p.SourceVersion, p.SourceDate, p.LastRefreshTime
{havingClause}
ORDER BY 
    p.PackageType ASC,
    COUNT(DISTINCT adv.AdvertisementID) DESC,
    COUNT(DISTINCT pr.ProgramName) DESC,
    p.LastRefreshTime DESC,
    p.Name ASC;
";

                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result.Rows.Count == 0)
                {
                    Logger.Warning("No packages found");
                    continue;
                }

                // Add decoded PackageType column and remove raw numeric column
                DataColumn decodedTypeColumn = result.Columns.Add("PackageType", typeof(string));
                int packageTypeRawIndex = result.Columns["PackageTypeRaw"].Ordinal;
                decodedTypeColumn.SetOrdinal(packageTypeRawIndex);

                foreach (DataRow row in result.Rows)
                {
                    row["PackageType"] = CMService.DecodePackageType(row["PackageTypeRaw"]);
                }

                // Remove raw numeric column
                result.Columns.Remove("PackageTypeRaw");

                Console.WriteLine(OutputFormatter.ConvertDataTable(result));

                Logger.Success($"Found {result.Rows.Count} package(s)");
            }

            return null;
        }
    }
}
