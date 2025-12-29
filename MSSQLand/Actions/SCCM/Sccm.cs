using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;
using System.Linq;

namespace MSSQLand.Actions.SCCM
{
    /// <summary>
    /// Retrieve global SCCM site information including site code, version, and URIs.
    /// </summary>
    internal class Sccm : BaseAction
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
                // Get all databases that start with CM_
                var databases = databaseContext.QueryService.ExecuteTable("SELECT name FROM sys.databases WHERE name LIKE 'CM_%';");
                
                if (databases.Rows.Count == 0)
                {
                    Logger.Warning("No databases found with SCCM naming pattern (CM_*)");
                    Logger.InfoNested("SCCM databases typically follow the naming pattern: CM_<SiteCode>");
                    return null;
                }

                Logger.Info($"Found {databases.Rows.Count} database(s) matching SCCM naming pattern");
                Logger.NewLine();

                int validSccmDatabases = 0;

                // Process each potential SCCM database
                foreach (DataRow row in databases.Rows)
                {
                    string sccmDatabase = row["name"].ToString();
                    string siteCode = sccmDatabase.Substring(3); // Remove "CM_" prefix
                    
                    Logger.Info($"Validating database: {sccmDatabase}");

                    // Validate this is actually an SCCM database by checking for core SCCM tables
                    string validationQuery = $@"
USE [{sccmDatabase}];

SELECT COUNT(*) AS TableCount
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_NAME IN ('Sites', 'SC_Component', 'RbacSecuredObject', 'Collections', 'vSMS_Boundary')
AND TABLE_SCHEMA = 'dbo';
";

                    try
                    {
                        var validation = databaseContext.QueryService.ExecuteTable(validationQuery);
                        int tableCount = Convert.ToInt32(validation.Rows[0]["TableCount"]);

                        if (tableCount < 3)
                        {
                            Logger.Warning($"Database '{sccmDatabase}' does not appear to be a valid SCCM database (missing core tables)");
                            Logger.NewLine();
                            continue;
                        }

                        validSccmDatabases++;
                        Logger.Success($"Confirmed SCCM database: {sccmDatabase} (Site Code: {siteCode})");
                        Logger.NewLine();

                    try
                    {
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
                            Logger.NewLine();
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
                            Logger.NewLine();
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
                            Logger.NewLine();
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
                            Logger.NewLine();
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
                            Logger.NewLine();
                        }

                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to query SCCM database '{sccmDatabase}': {ex.Message}");
                        Logger.ErrorNested("Ensure you have appropriate permissions to read SCCM tables");
                    }
                }

                Logger.NewLine();
                if (validSccmDatabases == 0)
                {
                    Logger.Warning("No valid SCCM databases were found");
                }
                else
                {
                    Logger.Success($"Successfully enumerated {validSccmDatabases} SCCM database(s)");
                }

                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to enumerate SCCM databases: {ex.Message}");
                Logger.Debug($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }
    }
}
