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

        public SccmService(QueryService queryService)
        {
            _queryService = queryService;
        }

        /// <summary>
        /// Gets all SCCM databases on the server.
        /// If the current execution database is an SCCM database, returns only that one.
        /// </summary>
        public List<string> GetSccmDatabases()
        {
            var databases = new List<string>();

            // Check if current execution database is an SCCM database
            string currentDb = _queryService.ExecutionDatabase;
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
    }
}
