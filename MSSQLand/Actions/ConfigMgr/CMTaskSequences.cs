// MSSQLand/Actions/ConfigMgr/CMTaskSequences.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.ConfigMgr
{
    /// <summary>
    /// Enumerate ConfigMgr Task Sequences with their properties and referenced content.
    /// 
    /// Task Sequences in ConfigMgr are ordered sets of automated steps used to perform complex IT operations.
    /// They are primarily used for OS deployment (bare-metal, refresh, replace scenarios) but can also
    /// automate software installation, patching, migrations, and configuration management.
    /// 
    /// A typical task sequence can contain hundreds of steps executed in order:
    /// - Boot into WinPE environment (using boot images)
    /// - Partition and format disks
    /// - Apply OS images (WIM files)
    /// - Install device drivers (driver packages)
    /// - Join domain and configure settings
    /// - Install applications and packages
    /// - Run PowerShell scripts
    /// - Apply Windows updates
    /// - Restart computer as needed
    /// 
    /// Task sequences reference multiple content types (boot images, OS images, driver packages, 
    /// applications, scripts) that are distributed to distribution points and downloaded during execution.
    /// Use 'sccm-tasksequence <PackageID>' to view all referenced content for a specific sequence.
    /// </summary>
    internal class CMTaskSequences : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "n", LongName = "name", Description = "Filter by task sequence name")]
        private string _name = "";

        [ArgumentMetadata(Position = 1, ShortName = "i", LongName = "packageid", Description = "Filter by PackageID")]
        private string _packageId = "";

        [ArgumentMetadata(Position = 2, ShortName = "d", LongName = "description", Description = "Filter by description")]
        private string _description = "";

        [ArgumentMetadata(Position = 3, LongName = "limit", Description = "Limit number of results (default: 50)")]
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

            _description = GetNamedArgument(named, "d", null)
                        ?? GetNamedArgument(named, "description", null)
                        ?? GetPositionalArgument(positional, 2, "");

            string limitStr = GetNamedArgument(named, "l", null)
                           ?? GetNamedArgument(named, "limit", null)
                           ?? GetPositionalArgument(positional, 3);
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
            if (!string.IsNullOrEmpty(_description))
                filterMsg += $" description: {_description}";

            Logger.TaskNested($"Enumerating ConfigMgr task sequences{(string.IsNullOrEmpty(filterMsg) ? "" : $" (filter:{filterMsg})")}");
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

                // TS_Type: 1 = Sequence (internal), 2 = Task Sequence (standard), 3 = Server Task Sequence (deprecated)
                string filterClause = "WHERE 1=1"; // Show all TS_Type values

                if (!string.IsNullOrEmpty(_name))
                {
                    filterClause += $" AND ts.Name LIKE '%{_name.Replace("'", "''")}%'";
                }

                if (!string.IsNullOrEmpty(_packageId))
                {
                    filterClause += $" AND ts.PackageID LIKE '%{_packageId.Replace("'", "''")}%'";
                }

                if (!string.IsNullOrEmpty(_description))
                {
                    filterClause += $" AND ts.Description LIKE '%{_description.Replace("'", "''")}%'";
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
                Logger.SuccessNested("Use 'sccm-tasksequence <PackageID>' to view detailed referenced content for a specific task sequence");
            }

            return null;
        }
    }
}
