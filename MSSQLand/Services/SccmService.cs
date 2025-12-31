using MSSQLand.Models;
using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Data;

namespace MSSQLand.Services
{
    /// <summary>
    /// Service for SCCM database detection and common operations.
    /// </summary>
    public class SccmService
    {
        private readonly QueryService _queryService;
        private readonly Server _server;

        public SccmService(QueryService queryService, Server server)
        {
            _queryService = queryService;
            _server = server;
        }

        /// <summary>
        /// Gets all SCCM databases on the server.
        /// If the current execution database is an SCCM database, returns only that one.
        /// </summary>
        public List<string> GetSccmDatabases()
        {
            var databases = new List<string>();

            // Check if current execution database is an SCCM database
            string currentDb = _queryService.ExecutionServer.Database;
            if (!string.IsNullOrEmpty(currentDb) && currentDb.StartsWith("CM_", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Debug($"Current execution database '{currentDb}' is an SCCM database");
                databases.Add(currentDb);
                return databases;
            }

            // Query for all SCCM databases
            try
            {
                var result = _queryService.ExecuteTable("SELECT name FROM sys.databases WHERE name LIKE 'CM_%';");
                foreach (DataRow row in result.Rows)
                {
                    databases.Add(row["name"].ToString());
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Failed to enumerate SCCM databases: {ex.Message}");
            }

            return databases;
        }

        /// <summary>
        /// Validates if a database is a valid SCCM database by checking for core tables.
        /// </summary>
        public bool ValidateSccmDatabase(string database, string[] requiredTables, int minimumTableCount = 2)
        {
            try
            {
                string tableList = string.Join("', '", requiredTables);
                string validationQuery = $@"
SELECT COUNT(*) AS TableCount
FROM [{database}].INFORMATION_SCHEMA.TABLES
WHERE TABLE_NAME IN ('{tableList}')
AND TABLE_SCHEMA = 'dbo';
";

                var validation = _queryService.ExecuteTable(validationQuery);
                int tableCount = Convert.ToInt32(validation.Rows[0]["TableCount"]);

                return tableCount >= minimumTableCount;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets validated SCCM databases with specific table requirements.
        /// </summary>
        public List<string> GetValidatedSccmDatabases(string[] requiredTables, int minimumTableCount = 2)
        {
            var validDatabases = new List<string>();
            var allDatabases = GetSccmDatabases();

            foreach (string database in allDatabases)
            {
                string siteCode = GetSiteCode(database);
                
                Logger.Debug($"Validating database: {database}");

                if (ValidateSccmDatabase(database, requiredTables, minimumTableCount))
                {
                    Logger.Debug($"Confirmed SCCM database: {database} (Site Code: {siteCode})");
                    validDatabases.Add(database);
                }
                else
                {
                    Logger.Debug($"Database '{database}' does not appear to be a valid SCCM database (missing required tables)");
                }
            }

            return validDatabases;
        }

        /// <summary>
        /// Extracts the site code from an SCCM database name.
        /// </summary>
        public static string GetSiteCode(string databaseName)
        {
            if (string.IsNullOrEmpty(databaseName) || !databaseName.StartsWith("CM_", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return databaseName.Substring(3);
        }

        /// <summary>
        /// Checks if the SCCM database has vSMS_* views or uses base tables.
        /// </summary>
        public bool HasSccmViews()
        {
            try
            {
                var result = _queryService.ExecuteScalar(@"
                    SELECT COUNT(*) 
                    FROM sys.views 
                    WHERE name LIKE 'vSMS_%'");
        
                int viewCount = Convert.ToInt32(result);
                bool hasViews = viewCount > 0;
        
                Logger.Debug($"Found {viewCount} vSMS_* views - Using {(hasViews ? "views" : "base tables")}");
                return hasViews;
            }
            catch
            {
                Logger.Debug("Failed to check for vSMS_* views, falling back to base tables");
                return false;
            }
        }

        /// <summary>
        /// Gets component status information, automatically using views or base tables depending on SCCM version.
        /// </summary>
        public DataTable GetComponentStatus(string database)
        {
            // Try views first (newer SCCM versions)
            if (HasSccmViews())
            {
                try
                {
                    Logger.Debug("Querying component status using vSMS_SC_Component_Status view");
                    return _queryService.ExecuteTable($@"
SELECT *
FROM [{database}].dbo.vSMS_SC_Component_Status
ORDER BY ComponentName;
");
                }
                catch (Exception ex)
                {
                    Logger.Debug($"View query failed, falling back to base tables: {ex.Message}");
                }
            }

            // Fallback to base tables (SCCM 2016 and older)
            Logger.Debug("Querying component status using SC_Component base table");
            return _queryService.ExecuteTable($@"
SELECT 
    ComponentName,
    Name AS MachineName,
    CASE Flags 
        WHEN 2 THEN 'OK'
        WHEN 5 THEN 'Warning'
        WHEN 6 THEN 'Error'
        ELSE 'Unknown'
    END AS Status,
    SiteNumber
FROM [{database}].dbo.SC_Component
ORDER BY ComponentName;
");
        }

        /// <summary>
        /// Gets site system roles, automatically using views or base tables depending on SCCM version.
        /// </summary>
        public DataTable GetSiteSystemRoles(string database)
        {
            // Try views first (newer SCCM versions)
            if (HasSccmViews())
            {
                try
                {
                    Logger.Debug("Querying site system roles using vSMS_SC_SiteSystemRole view");
                    return _queryService.ExecuteTable($@"
SELECT *
FROM [{database}].dbo.vSMS_SC_SiteSystemRole
ORDER BY ServerName, RoleName;
");
                }
                catch (Exception ex)
                {
                    Logger.Debug($"View query failed, falling back to base tables: {ex.Message}");
                }
            }

            // Fallback to base tables (SCCM 2016 and older)
            Logger.Debug("Querying site system roles using SC_SysResUse base table");
            return _queryService.ExecuteTable($@"
SELECT 
    sr.NALPath,
    sr.RoleTypeID,
    CASE sr.RoleTypeID
        WHEN 2 THEN 'SMS Provider'
        WHEN 3 THEN 'Distribution Point'
        WHEN 4 THEN 'Management Point'
        WHEN 5 THEN 'Fallback Status Point'
        WHEN 6 THEN 'Site Server'
        WHEN 11 THEN 'Software Update Point'
        WHEN 16 THEN 'Application Catalog Web Service Point'
        WHEN 17 THEN 'Application Catalog Website Point'
        WHEN 21 THEN 'Reporting Services Point'
        WHEN 22 THEN 'Enrollment Point'
        WHEN 23 THEN 'Enrollment Proxy Point'
        WHEN 25 THEN 'Asset Intelligence Synchronization Point'
        WHEN 27 THEN 'State Migration Point'
        WHEN 28 THEN 'System Health Validator Point'
        WHEN 31 THEN 'Out Of Band Service Point'
        ELSE CAST(sr.RoleTypeID AS VARCHAR(10))
    END AS RoleName,
    sr.NALResType
FROM [{database}].dbo.SC_SysResUse sr
ORDER BY sr.NALPath, sr.RoleTypeID;
");
        }

        /// <summary>
        /// Gets network boundaries, automatically using views or base tables depending on SCCM version.
        /// </summary>
        public DataTable GetBoundaries(string database)
        {
            // Try views first (newer SCCM versions)
            if (HasSccmViews())
            {
                try
                {
                    Logger.Debug("Querying boundaries using vSMS_Boundary view");
                    return _queryService.ExecuteTable($@"
SELECT *
FROM [{database}].dbo.vSMS_Boundary
ORDER BY BoundaryType, DisplayName;
");
                }
                catch (Exception ex)
                {
                    Logger.Debug($"View query failed, falling back to base tables: {ex.Message}");
                }
            }

            // Fallback to base tables (SCCM 2016 and older)
            Logger.Debug("Querying boundaries using BoundaryEx base table");
            return _queryService.ExecuteTable($@"
SELECT 
    b.Name AS DisplayName,
    CASE b.BoundaryType
        WHEN 0 THEN 'IP Subnet'
        WHEN 1 THEN 'AD Site'
        WHEN 2 THEN 'IPv6 Prefix'
        WHEN 3 THEN 'IP Range'
        ELSE CAST(b.BoundaryType AS VARCHAR(10))
    END AS BoundaryType,
    b.Value,
    bg.Name AS BoundaryGroup,
    b.BoundaryID
FROM [{database}].dbo.BoundaryEx b
LEFT JOIN [{database}].dbo.BoundaryGroupMembers bgm ON b.BoundaryID = bgm.BoundaryID
LEFT JOIN [{database}].dbo.BoundaryGroup bg ON bgm.GroupID = bg.GroupID
ORDER BY b.BoundaryType, b.Name;
");
        }

        /// <summary>
        /// Gets distribution points, automatically using views or base tables depending on SCCM version.
        /// </summary>
        public DataTable GetDistributionPoints(string database)
        {
            // Try views first (newer SCCM versions)
            if (HasSccmViews())
            {
                try
                {
                    Logger.Debug("Querying distribution points using vSMS_DistributionPoint view");
                    return _queryService.ExecuteTable($@"
SELECT *
FROM [{database}].dbo.vSMS_DistributionPoint
ORDER BY ServerName;
");
                }
                catch (Exception ex)
                {
                    Logger.Debug($"View query failed, falling back to base tables: {ex.Message}");
                }
            }

            // Fallback to base tables (SCCM 2016 and older)
            Logger.Debug("Querying distribution points using DistributionPoints base table");
            return _queryService.ExecuteTable($@"
SELECT 
    ServerName,
    SMSSiteCode AS SiteCode,
    NALPath,
    Description,
    CASE WHEN IsPXE = 1 THEN 'Yes' ELSE 'No' END AS PXE_Enabled,
    CASE WHEN IsActive = 1 THEN 'Active' ELSE 'Inactive' END AS Status
FROM [{database}].dbo.DistributionPoints
ORDER BY ServerName;
");
        }
    }
}
