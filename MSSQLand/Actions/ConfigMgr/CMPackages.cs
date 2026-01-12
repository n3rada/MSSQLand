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
        [ArgumentMetadata(Position = 0, ShortName = "f", LongName = "filter", Description = "Filter by package name")]
        private string _filter = "";

        [ArgumentMetadata(Position = 1, ShortName = "s", LongName = "source", Description = "Filter by package source path")]
        private string _sourcePath = "";

        [ArgumentMetadata(Position = 2,  LongName = "limit", Description = "Limit number of results (default: 50)")]
        private int _limit = 50;

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            _filter = GetNamedArgument(named, "f", null)
                   ?? GetNamedArgument(named, "filter", null)
                   ?? GetPositionalArgument(positional, 0, "");

            _sourcePath = GetNamedArgument(named, "s", null)
                       ?? GetNamedArgument(named, "source", null)
                       ?? GetPositionalArgument(positional, 1, "");

            string limitStr = GetNamedArgument(named, "l", null)
                           ?? GetNamedArgument(named, "limit", null)
                           ?? GetPositionalArgument(positional, 2);
            if (!string.IsNullOrEmpty(limitStr))
            {
                _limit = int.Parse(limitStr);
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            string filterMsg = "";
            if (!string.IsNullOrEmpty(_filter))
                filterMsg += $" name: {_filter}";
            if (!string.IsNullOrEmpty(_sourcePath))
                filterMsg += $" source: {_sourcePath}";

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

                string filterClause = "WHERE 1=1";

                if (!string.IsNullOrEmpty(_filter))
                {
                    filterClause += $" AND p.Name LIKE '%{_filter.Replace("'", "''")}%'";
                }

                if (!string.IsNullOrEmpty(_sourcePath))
                {
                    filterClause += $" AND p.PkgSourcePath LIKE '%{_sourcePath.Replace("'", "''")}%'";
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
