using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.SCCM
{
    /// <summary>
    /// Display SCCM site information including site code, version, build, database server, and management point details.
    /// Use this for initial reconnaissance to identify SCCM infrastructure components, site hierarchy, and installed version.
    /// Shows distribution points, site systems, and component servers for infrastructure mapping.
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

            SccmService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            try
            {
                // Get SCCM databases
                var databases = sccmService.GetSccmDatabases();
                
                if (databases.Count == 0)
                {
                    Logger.Warning("No valid SCCM databases found");
                    return null;
                }

                if (databases.Count > 1)
                {
                    Logger.TaskNested($"Multiple SCCM databases detected: {databases.Count}");
                    foreach (string db in databases)
                    {
                        Logger.InfoNested($"- {db}");
                    }
                }

                // Process each validated SCCM database
                foreach (string sccmDatabase in databases)
                {
                    Logger.NewLine();
                    string siteCode = SccmService.GetSiteCode(sccmDatabase);
                    Logger.Info($"Enumerating SCCM database: {sccmDatabase} (Site Code: {siteCode})");

                    // Get site information
                    string siteInfoQuery = $"SELECT * FROM [{sccmDatabase}].dbo.Sites;";

                    var siteInfo = databaseContext.QueryService.ExecuteTable(siteInfoQuery);
                    
                    if (siteInfo.Rows.Count > 0)
                    {
                        // Filter to only useful columns
                        var filteredSiteInfo = new DataTable();
                        string[] columnsToShow = { "SiteCode", "SiteName", "Version", "SiteServer", "InstallDir", "DefaultMP" };
                        
                        foreach (string col in columnsToShow)
                        {
                            if (siteInfo.Columns.Contains(col))
                            {
                                filteredSiteInfo.Columns.Add(col, siteInfo.Columns[col].DataType);
                            }
                        }
                        
                        foreach (DataRow row in siteInfo.Rows)
                        {
                            var newRow = filteredSiteInfo.NewRow();
                            foreach (DataColumn col in filteredSiteInfo.Columns)
                            {
                                newRow[col.ColumnName] = row[col.ColumnName];
                            }
                            filteredSiteInfo.Rows.Add(newRow);
                        }
                        
                        Logger.Success("Site Information");
                        Console.WriteLine(OutputFormatter.ConvertDataTable(filteredSiteInfo));
                    }

                    // Get component status (handles both views and base tables)
                    try
                    {
                        var components = sccmService.GetComponentStatus(sccmDatabase);
                        
                        if (components.Rows.Count > 0)
                        {
                            Logger.Success("SCCM Components");
                            Console.WriteLine(OutputFormatter.ConvertDataTable(components));
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Could not query SCCM components: {ex.Message}");
                    }

                    // Get site system roles (handles both views and base tables)
                    try
                    {
                        var siteSystems = sccmService.GetSiteSystemRoles(sccmDatabase);
                        
                        if (siteSystems.Rows.Count > 0)
                        {
                            Logger.Success("Site System Roles");
                            Console.WriteLine(OutputFormatter.ConvertDataTable(siteSystems));
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Could not query site system roles: {ex.Message}");
                    }

                    // Get network boundaries (handles both views and base tables)
                    try
                    {
                        var boundaries = sccmService.GetBoundaries(sccmDatabase);
                        
                        if (boundaries.Rows.Count > 0)
                        {
                            Logger.Success("Network Boundaries");
                            Console.WriteLine(OutputFormatter.ConvertDataTable(boundaries));
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Could not query network boundaries: {ex.Message}");
                    }

                    // Get distribution points (handles both views and base tables)
                    try
                    {
                        var distributionPoints = sccmService.GetDistributionPoints(sccmDatabase);
                        
                        if (distributionPoints.Rows.Count > 0)
                        {
                            Logger.Success("Distribution Points");
                            Console.WriteLine(OutputFormatter.ConvertDataTable(distributionPoints));
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Could not query distribution points: {ex.Message}");
                    }
                }

                Logger.Success($"Successfully enumerated {databases.Count} SCCM database(s)");

                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to enumerate SCCM databases: {ex.Message}");
                Logger.Trace($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }
    }
}
