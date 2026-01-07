using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text;

namespace MSSQLand.Actions.SCCM
{
    /// <summary>
    /// Enumerate SCCM programs (legacy package execution configurations) with command lines and run behavior.
    /// Use this to view program details including install commands, working directories, and execution flags.
    /// Programs define how packages are executed - shows command lines, user context, UI mode, and restart behavior.
    /// For modern application deployments, use sccm-apps instead.
    /// </summary>
    internal class SccmPrograms : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "p", LongName = "package", Description = "Filter by PackageID")]
        private string _packageId = "";

        [ArgumentMetadata(Position = 1, ShortName = "n", LongName = "name", Description = "Filter by program name")]
        private string _programName = "";

        [ArgumentMetadata(Position = 2, LongName = "limit", Description = "Limit number of results (default: 50)")]
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
            if (!string.IsNullOrEmpty(_packageId))
                filterMsg += $" package: {_packageId}";
            if (!string.IsNullOrEmpty(_programName))
                filterMsg += $" name: {_programName}";

            Logger.TaskNested($"Enumerating SCCM programs{(string.IsNullOrEmpty(filterMsg) ? "" : $" (filter:{filterMsg})")}");
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

                string filterClause = "WHERE 1=1";

                if (!string.IsNullOrEmpty(_packageId))
                {
                    filterClause += $" AND pr.PackageID LIKE '%{_packageId.Replace("'", "''")}%'";
                }

                if (!string.IsNullOrEmpty(_programName))
                {
                    filterClause += $" AND pr.ProgramName LIKE '%{_programName.Replace("'", "''")}%'";
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
                            row["DecodedFlags"] = DecodeProgramFlags(flags);
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

            Logger.Success("Program enumeration completed");
            return null;
        }

        /// <summary>
        /// Decodes ProgramFlags bitmask into human-readable semicolon-separated string.
        /// </summary>
        private static string DecodeProgramFlags(uint flags)
        {
            var flagsList = new List<string>();

            var flagDefinitions = new Dictionary<uint, string>
            {
                { 0x00000001, "AUTHORIZED_DYNAMIC_INSTALL" },
                { 0x00000002, "USECUSTOMPROGRESSMSG" },
                { 0x00000010, "DEFAULT_PROGRAM" },
                { 0x00000020, "DISABLEMOMALERTONRUNNING" },
                { 0x00000040, "MOMALERTONFAIL" },
                { 0x00000080, "RUN_DEPENDANT_ALWAYS" },
                { 0x00000100, "WINDOWS_CE" },
                { 0x00000400, "COUNTDOWN" },
                { 0x00001000, "DISABLED" },
                { 0x00002000, "UNATTENDED" },
                { 0x00004000, "USERCONTEXT" },
                { 0x00008000, "ADMINRIGHTS" },
                { 0x00010000, "EVERYUSER" },
                { 0x00020000, "NOUSERLOGGEDIN" },
                { 0x00040000, "OKTOQUIT" },
                { 0x00080000, "OKTOREBOOT" },
                { 0x00100000, "USEUNCPATH" },
                { 0x00200000, "PERSISTCONNECTION" },
                { 0x00400000, "RUNMINIMIZED" },
                { 0x00800000, "RUNMAXIMIZED" },
                { 0x01000000, "HIDEWINDOW" },
                { 0x02000000, "OKTOLOGOFF" },
                { 0x04000000, "RUNACCOUNT" },
                { 0x08000000, "ANY_PLATFORM" },
                { 0x10000000, "STILL_RUNNING" },
                { 0x20000000, "SUPPORT_UNINSTALL" },
                { 0x40000000, "PLATFORM_NOT_SUPPORTED" },
                { 0x80000000, "SHOW_IN_ARP" }
            };

            foreach (var kvp in flagDefinitions)
            {
                if ((flags & kvp.Key) == kvp.Key)
                {
                    flagsList.Add(kvp.Value);
                }
            }

            return flagsList.Count > 0 ? string.Join("; ", flagsList) : "None";
        }
    }
}
