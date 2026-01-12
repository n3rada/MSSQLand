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
    /// Packages are the legacy deployment model - use sccm-apps for modern application deployments.
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

        [ArgumentMetadata(Position = 8, LongName = "limit", Description = "Limit number of results (default: 50)")]
        private int _limit = 50;

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            _name = GetNamedArgument(named, "n", null)
                 ?? GetNamedArgument(named, "name", null)
                 ?? GetPositionalArgument(positional, 0, "");

            _sourcePath = GetNamedArgument(named, "s", null)
                       ?? GetNamedArgument(named, "source", null)
                       ?? GetPositionalArgument(positional, 1, "");

            _manufacturer = GetNamedArgument(named, "m", null)
                         ?? GetNamedArgument(named, "manufacturer", null)
                         ?? GetPositionalArgument(positional, 2, "");

            _version = GetNamedArgument(named, "v", null)
                    ?? GetNamedArgument(named, "version", null)
                    ?? GetPositionalArgument(positional, 3, "");

            _packageType = GetNamedArgument(named, "t", null)
                        ?? GetNamedArgument(named, "type", null)
                        ?? GetPositionalArgument(positional, 4, "");

            _withPrograms = named.ContainsKey("with-programs");
            _withDeployments = named.ContainsKey("with-deployments");
            _noSource = named.ContainsKey("no-source");

            string limitStr = GetNamedArgument(named, "l", null)
                           ?? GetNamedArgument(named, "limit", null)
                           ?? GetPositionalArgument(positional, 8);
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
{filterClause}
GROUP BY 
    p.PackageID, p.Name, p.Description, p.PkgSourcePath, 
    p.Manufacturer, p.Version, p.PackageType,
    p.StoredPkgPath, p.SourceVersion, p.SourceDate, p.LastRefreshTime
ORDER BY p.Name;
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
