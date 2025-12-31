using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.SCCM
{
    /// <summary>
    /// Enumerate SCCM applications with installation details and requirements.
    /// Applications are the newer deployment model compared to packages.
    /// </summary>
    internal class SccmApplications : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "f", LongName = "filter", Description = "Filter by application name")]
        private string _filter = "";

        [ArgumentMetadata(Position = 1, ShortName = "l", LongName = "limit", Description = "Limit number of results (default: 50)")]
        private int _limit = 50;

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            _filter = GetNamedArgument(named, "f", null)
                   ?? GetNamedArgument(named, "filter", null)
                   ?? GetPositionalArgument(positional, 0, "");

            string limitStr = GetNamedArgument(named, "l", null)
                           ?? GetNamedArgument(named, "limit", null)
                           ?? GetPositionalArgument(positional, 1);
            if (!string.IsNullOrEmpty(limitStr))
            {
                _limit = int.Parse(limitStr);
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            string filterMsg = !string.IsNullOrEmpty(_filter) ? $" (filter: {_filter})" : "";
            Logger.TaskNested($"Enumerating SCCM applications{filterMsg}");
            Logger.TaskNested($"Limit: {_limit}");

            SccmService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            string[] requiredTables = { "v_ApplicationAssignment" };
            var databases = sccmService.GetValidatedSccmDatabases(requiredTables, 1);

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

                string filterClause = string.IsNullOrEmpty(_filter)
                    ? "WHERE ci.CIType_ID = 10"
                    : $"WHERE ci.CIType_ID = 10 AND ci.DisplayName LIKE '%{_filter}%'";

                string query = $@"
SELECT TOP {_limit}
    ci.CI_ID,
    ci.DisplayName,
    ci.Description,
    ci.SoftwareVersion,
    ci.Publisher,
    ci.IsDeployed,
    ci.IsEnabled,
    ci.IsExpired,
    ci.IsSuperseded,
    ci.NumberOfDeployments,
    ci.NumberOfDevicesWithApp,
    ci.NumberOfUsersWithApp,
    ci.CreatedBy,
    ci.DateCreated,
    ci.DateLastModified,
    ci.SourceSite,
    dt.Technology AS DeploymentTechnology,
    dt.ContentLocation,
    dt.InstallCommandLine,
    dt.UninstallCommandLine,
    dt.ExecutionContext
FROM [{db}].dbo.v_ConfigurationItems ci
LEFT JOIN [{db}].dbo.v_DeploymentType dt ON ci.ModelName = dt.AppModelName
{filterClause}
ORDER BY ci.IsDeployed DESC, ci.DateCreated DESC;
";

                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result.Rows.Count == 0)
                {
                    Logger.Warning("No applications found");
                    continue;
                }

                Console.WriteLine(OutputFormatter.ConvertDataTable(result));

                Logger.Success($"Found {result.Rows.Count} application(s)");
            }

            Logger.Success("Application enumeration completed");
            return null;
        }
    }
}
