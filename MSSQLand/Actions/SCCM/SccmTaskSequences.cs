using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.SCCM
{
    /// <summary>
    /// Enumerate SCCM Task Sequences with their properties and referenced content.
    /// Use this to view OS deployment workflows, imaging sequences, and automated installation procedures.
    /// Task sequences contain multiple steps (install OS, drivers, applications, scripts) executed in order.
    /// Shows sequence names, boot images, referenced packages/applications, and execution settings.
    /// </summary>
    internal class SccmTaskSequences : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "n", LongName = "name", Description = "Filter by task sequence name")]
        private string _name = "";

        [ArgumentMetadata(Position = 1, ShortName = "i", LongName = "packageid", Description = "Filter by PackageID")]
        private string _packageId = "";

        [ArgumentMetadata(Position = 2, LongName = "limit", Description = "Limit number of results (default: 50)")]
        private int _limit = 50;

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            _name = GetNamedArgument(named, "n", null)
                 ?? GetNamedArgument(named, "name", null)
                 ?? GetPositionalArgument(positional, 0, "");

            _packageId = GetNamedArgument(named, "i", null)
                      ?? GetNamedArgument(named, "packageid", null)
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
            if (!string.IsNullOrEmpty(_name))
                filterMsg += $" name: {_name}";
            if (!string.IsNullOrEmpty(_packageId))
                filterMsg += $" packageid: {_packageId}";

            Logger.TaskNested($"Enumerating SCCM task sequences{(string.IsNullOrEmpty(filterMsg) ? "" : $" (filter:{filterMsg})")}");
            Logger.TaskNested($"Limit: {_limit}");

            SccmService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            var databases = sccmService.GetSccmDatabases();

            if (databases.Count == 0)
            {
                Logger.Warning("No SCCM databases found");
                return null;
            }

            foreach (string db in databases)
            {
                string siteCode = SccmService.GetSiteCode(db);

                Logger.NewLine();
                Logger.Info($"SCCM database: {db} (Site Code: {siteCode})");

                string filterClause = "WHERE ts.Type = 4"; // Type 4 = Task Sequence

                if (!string.IsNullOrEmpty(_name))
                {
                    filterClause += $" AND ts.Name LIKE '%{_name.Replace("'", "''")}%'";
                }

                if (!string.IsNullOrEmpty(_packageId))
                {
                    filterClause += $" AND ts.PackageID LIKE '%{_packageId.Replace("'", "''")}%'";
                }

                string query = $@"
SELECT TOP {_limit}
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
    CASE 
        WHEN ts.BootImageID IS NOT NULL AND ts.BootImageID != '' 
        THEN ts.BootImageID 
        ELSE 'None' 
    END AS BootImageID,
    bi.Name AS BootImageName,
    CASE 
        WHEN ts.SecuredScopeID IS NOT NULL 
        THEN 'Yes' 
        ELSE 'No' 
    END AS IsSecured,
    ts.SecuredScopeID,
    (
        SELECT COUNT(*) 
        FROM [{db}].dbo.v_TaskSequenceReferencesInfo ref 
        WHERE ref.PackageID = ts.PackageID
    ) AS ReferencedContentCount,
    LEN(ts.Sequence) AS SequenceXMLSize
FROM [{db}].dbo.v_TaskSequencePackage ts
LEFT JOIN [{db}].dbo.v_BootImagePackage bi ON ts.BootImageID = bi.PackageID
{filterClause}
ORDER BY ts.Name;
";

                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result.Rows.Count == 0)
                {
                    Logger.Warning("No task sequences found");
                    continue;
                }

                Console.WriteLine(OutputFormatter.ConvertDataTable(result));

                Logger.Success($"Found {result.Rows.Count} task sequence(s)");

                // Show referenced content summary if available
                foreach (DataRow row in result.Rows)
                {
                    int refCount = row["ReferencedContentCount"] != DBNull.Value ? Convert.ToInt32(row["ReferencedContentCount"]) : 0;
                    if (refCount > 0)
                    {
                        string pkgId = row["PackageID"].ToString();
                        string name = row["Name"].ToString();
                        Logger.Info($"  {name} ({pkgId}) references {refCount} package(s)/application(s)");
                    }
                }
            }

            Logger.Success("Task sequence enumeration completed");
            return null;
        }
    }
}
