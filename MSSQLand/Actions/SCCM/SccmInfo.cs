using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.SCCM
{
    /// <summary>
    /// Retrieve global SCCM site information including site code, version, and URIs.
    /// </summary>
    internal class SccmInfo : BaseAction
    {
        public override void ValidateArguments(string[] args)
        {
            // No additional arguments needed
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Detecting SCCM databases");

            try
            {
                // Get and validate SCCM databases
                string[] requiredTables = { "Sites", "SC_Component", "RbacSecuredObject", "Collections", "vSMS_Boundary" };
                var databases = databaseContext.SccmService.GetValidatedSccmDatabases(requiredTables, 3);
                
                if (databases.Count == 0)
                {
                    Logger.Warning("No valid SCCM databases found");
                    return null;
                }

                if (databases.Count > 1)
                {
                    Logger.Info($"Multiple SCCM databases detected: {databases.Count}");
                    Logger.NewLine();
                }

                // Process each validated SCCM database
                foreach (string sccmDatabase in databases)
                {
                    string siteCode = SccmService.GetSiteCode(sccmDatabase);
                    Logger.Info($"Enumerating SCCM database: {sccmDatabase} (Site Code: {siteCode})");
                    Logger.NewLine();

                    // Get site information
                    string siteInfoQuery = $@"
SELECT 
    SiteCode,
    SiteName,
    Version,
    BuildNumber,
    InstallDir,
    ServerName,
    ReportServerInstance,
    SiteServer
FROM [{sccmDatabase}].dbo.Sites
WHERE SiteCode = '{siteCode}';
";

                    var siteInfo = databaseContext.QueryService.ExecuteTable(siteInfoQuery);
                    
                    if (siteInfo.Rows.Count > 0)
                    {
                        Logger.Success("Site Information");
                        Console.WriteLine(OutputFormatter.ConvertDataTable(siteInfo));
                    }

                    // Get component servers
                    string componentQuery = $@"
SELECT 
    ServerName,
    SiteCode,
    ComponentName,
    Status,
    Availability
FROM [{sccmDatabase}].dbo.vSMS_SC_Component_Status
WHERE SiteCode = '{siteCode}'
ORDER BY ComponentName;
";

                    var components = databaseContext.QueryService.ExecuteTable(componentQuery);
                    
                    if (components.Rows.Count > 0)
                    {
                        Logger.Success("SCCM Components");
                        Console.WriteLine(OutputFormatter.ConvertDataTable(components));
                    }

                    // Get site system servers
                    string siteSystemsQuery = $@"
SELECT 
    ServerName,
    RoleName,
    SiteCode
FROM [{sccmDatabase}].dbo.vSMS_SC_SiteSystemRole
WHERE SiteCode = '{siteCode}'
ORDER BY ServerName, RoleName;
";

                    var siteSystems = databaseContext.QueryService.ExecuteTable(siteSystemsQuery);
                    
                    if (siteSystems.Rows.Count > 0)
                    {
                        Logger.Success("Site System Roles");
                        Console.WriteLine(OutputFormatter.ConvertDataTable(siteSystems));
                    }

                    // Get site boundaries
                    string boundariesQuery = $@"
SELECT 
    DisplayName,
    BoundaryType,
    Value,
    SiteSystems
FROM [{sccmDatabase}].dbo.vSMS_Boundary
ORDER BY BoundaryType, DisplayName;
";

                    var boundaries = databaseContext.QueryService.ExecuteTable(boundariesQuery);
                    
                    if (boundaries.Rows.Count > 0)
                    {
                        Logger.Success("Network Boundaries");
                        Console.WriteLine(OutputFormatter.ConvertDataTable(boundaries));
                    }

                    // Get distribution points
                    string dpQuery = $@"
SELECT 
    ServerName,
    NALPath,
    SiteCode,
    IsActive,
    IsPullDP
FROM [{sccmDatabase}].dbo.vSMS_DistributionPoint
WHERE SiteCode = '{siteCode}'
ORDER BY ServerName;
";

                    var distributionPoints = databaseContext.QueryService.ExecuteTable(dpQuery);
                    
                    if (distributionPoints.Rows.Count > 0)
                    {
                        Logger.Success("Distribution Points");
                        Console.WriteLine(OutputFormatter.ConvertDataTable(distributionPoints));
                    }
                }

                Logger.NewLine();
                Logger.Success($"Successfully enumerated {databases.Count} SCCM database(s)");

                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to enumerate SCCM databases: {ex.Message}");
                Logger.TraceNested($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }
    }
}
