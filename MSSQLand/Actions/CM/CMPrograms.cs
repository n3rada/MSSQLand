using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text;

namespace MSSQLand.Actions.CM
{
    /// <summary>
    /// Enumerate ConfigMgr programs (legacy package execution configurations) with command lines and run behavior.
    /// Use this to view program details including install commands, working directories, and execution flags.
    /// Programs define how packages are executed - shows command lines, user context, UI mode, and restart behavior.
    /// For modern application deployments, use sccm-apps instead.
    /// </summary>
    internal class CMPrograms : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "p", LongName = "package", Description = "Filter by PackageID")]
        private string _packageId = "";

        [ArgumentMetadata(Position = 1, ShortName = "n", LongName = "name", Description = "Filter by program name")]
        private string _programName = "";

        [ArgumentMetadata(Position = 2, ShortName = "c", LongName = "commandline", Description = "Search within command line")]
        private string _commandLine = "";

        [ArgumentMetadata(Position = 3, LongName = "limit", Description = "Limit number of results (default: 50)")]
        private int _limit = 50;

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            _packageId = GetNamedArgument(named, "p", null)
                      ?? GetNamedArgument(named, "package", null)
                      ?? GetPositionalArgument(positional, 0, "");

            _programName = GetNamedArgument(named, "n", null)
                        ?? GetNamedArgument(named, "name", null)
                        ?? GetPositionalArgument(positional, 1, "");

            _commandLine = GetNamedArgument(named, "c", null)
                        ?? GetNamedArgument(named, "commandline", null)
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
            if (!string.IsNullOrEmpty(_packageId))
                filterMsg += $" package: {_packageId}";
            if (!string.IsNullOrEmpty(_programName))
                filterMsg += $" name: {_programName}";
            if (!string.IsNullOrEmpty(_commandLine))
                filterMsg += $" commandline: {_commandLine}";

            Logger.TaskNested($"Enumerating ConfigMgr programs{(string.IsNullOrEmpty(filterMsg) ? "" : $" (filter:{filterMsg})")}");
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

                if (!string.IsNullOrEmpty(_packageId))
                {
                    filterClause += $" AND pr.PackageID LIKE '%{_packageId.Replace("'", "''")}%'";
                }

                if (!string.IsNullOrEmpty(_programName))
                {
                    filterClause += $" AND pr.ProgramName LIKE '%{_programName.Replace("'", "''")}%'";
                }

                if (!string.IsNullOrEmpty(_commandLine))
                {
                    filterClause += $" AND pr.CommandLine LIKE '%{_commandLine.Replace("'", "''")}%'";
                }

                string query = $@"
SELECT TOP {_limit}
    pr.PackageID,
    pk.Name AS PackageName,
    pr.ProgramName,
    pr.CommandLine,
    pr.WorkingDirectory,
    pr.Comment,
    pr.ProgramFlags,
    pr.Duration,
    pr.DiskSpaceRequired,
    pr.Requirements,
    pr.DependentProgram,
    pr.DriveLetter,
    CASE 
        WHEN pr.ProgramFlags & 0x00000001 = 0x00000001 THEN 'Yes'
        ELSE 'No'
    END AS AuthorizedDynamicInstall,
    CASE 
        WHEN pr.ProgramFlags & 0x00001000 = 0x00001000 THEN 'Disabled'
        ELSE 'Enabled'
    END AS Status
FROM [{db}].dbo.v_Program pr
LEFT JOIN [{db}].dbo.v_Package pk ON pr.PackageID = pk.PackageID
{filterClause}
ORDER BY pk.Name, pr.ProgramName;
";

                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result.Rows.Count == 0)
                {
                    Logger.Warning("No programs found");
                    continue;
                }

                // Add decoded flags column before ProgramFlags
                DataColumn decodedFlagsColumn = result.Columns.Add("DecodedFlags", typeof(string));
                int programFlagsIndex = result.Columns["ProgramFlags"].Ordinal;
                decodedFlagsColumn.SetOrdinal(programFlagsIndex);

                foreach (DataRow row in result.Rows)
                {
                    if (row["ProgramFlags"] != DBNull.Value)
                    {
                        try
                        {
                            // ProgramFlags is stored as INT (signed) in SQL Server
                            // Convert to Int32 first, then cast to UInt32 for bitwise operations
                            int signedFlags = Convert.ToInt32(row["ProgramFlags"]);
                            uint flags = unchecked((uint)signedFlags);
                            row["DecodedFlags"] = CMService.DecodeProgramFlags(flags);
                        }
                        catch (Exception ex)
                        {
                            row["DecodedFlags"] = $"Error: {ex.Message}";
                            Logger.Debug($"Failed to decode flags for {row["ProgramName"]}: {ex.Message}");
                        }
                    }
                }

                Console.WriteLine(OutputFormatter.ConvertDataTable(result));

                Logger.Success($"Found {result.Rows.Count} program(s)");
            }

            return null;
        }
    }
}
