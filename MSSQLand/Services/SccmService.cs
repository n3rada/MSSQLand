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
        /// Checks if the SCCM database has vSMS_* views (newer versions) or uses base tables (SCCM 2016 and older).
        /// Uses SQL Server version detection: SQL Server 2016 (version 13) and older use base tables.
        /// For linked servers, uses the execution server's version; otherwise uses the connection server's version.
        /// </summary>
        public bool HasSccmViews()
        {
            // Use execution server (handles both direct connection and linked server chains)
            Server executionServer = _queryService.ExecutionServer;
            bool usesViews = !executionServer.Legacy;
            
            Logger.Debug($"SQL Server version {executionServer.MajorVersion} (Legacy: {executionServer.Legacy}) - Using {(usesViews ? "vSMS_* views" : "base tables")}");
            return usesViews;
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
    MachineName,
    Status,
    Errors,
    Warnings,
    Infos
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
    SiteCode,
    RoleName,
    NALPath,
    SiteSystem
FROM [{database}].dbo.SC_SysResUse
ORDER BY SiteSystem, RoleName;
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
    b.DisplayName,
    b.BoundaryType,
    b.Value,
    bg.GroupName,
    b.SiteCode
FROM [{database}].dbo.BoundaryEx b
LEFT JOIN [{database}].dbo.BoundaryGroupMembers bgm ON b.BoundaryID = bgm.BoundaryID
LEFT JOIN [{database}].dbo.BoundaryGroup bg ON bgm.GroupID = bg.GroupID
ORDER BY b.BoundaryType, b.DisplayName;
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
    SiteCode,
    NALPath,
    Description
FROM [{database}].dbo.DistributionPoints
ORDER BY ServerName;
");
        }
    }
}
