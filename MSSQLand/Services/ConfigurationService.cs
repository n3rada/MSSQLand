using MSSQLand.Models;
using MSSQLand.Utilities;
using System;
using System.Data;

namespace MSSQLand.Services
{
    public class ConfigurationService
    {
        private readonly QueryService _queryService;
        private readonly Server _server;

        public ConfigurationService(QueryService queryService, Server server)
        {
            _queryService = queryService;
            _server = server;
        }

        

        public bool CheckAssembly(string assemblyName)
        {
            string query = $"SELECT name FROM master.sys.assemblies WHERE name='{assemblyName}';";

            return _queryService.ExecuteScalar(query)?.ToString() == assemblyName;
        }

        public bool CheckAssemblyModules(string assemblyName)
        {
            string query = $"SELECT * FROM master.sys.assembly_modules;";

            string result = MarkdownFormatter.ConvertDataTableToMarkdownTable(_queryService.ExecuteTable(query)).ToLower();

            if (result.Contains(assemblyName.ToLower()))
            {
                Logger.Success($"Assembly '{assemblyName}' has modules");
                return true;
            }
            return false;
        }

        public bool CheckTrustedAssembly(string assemblyName)
        {
            try
            {
                // Query to retrieve all trusted assemblies
                string query = "SELECT description FROM master.sys.trusted_assemblies;";
                DataTable trustedAssembliesTable = _queryService.ExecuteTable(query);

                if (trustedAssembliesTable.Rows.Count == 0)
                {
                    Logger.Warning("No trusted assemblies found");
                    return false;
                }

                // Log all trusted assemblies for debugging
                Logger.Debug("Trusted assemblies:");
                foreach (DataRow row in trustedAssembliesTable.Rows)
                {
                    string description = row["description"].ToString();
                    Logger.DebugNested(description);

                    string name = description.Split(',')[0];

                    // Check if the assemblyName is a substring of the description
                    if (name == assemblyName)
                    {
                        Logger.Success($"Assembly '{assemblyName}' is trusted");
                        return true;
                    }
                }

                Logger.Warning($"Assembly '{assemblyName}' is not trusted");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error retrieving trusted assemblies: {ex.Message}");
                return false;
            }
        }


        public bool CheckProcedures(string procedureName)
        {
            try
            {
                // Query to retrieve all trusted assemblies
                string query = "SELECT SCHEMA_NAME(schema_id), name, type FROM sys.procedures;";
                DataTable trustedAssembliesTable = _queryService.ExecuteTable(query);

                if (trustedAssembliesTable.Rows.Count == 0)
                {
                    Logger.Warning("No procedures found");
                    return false;
                }

                // Log all trusted assemblies for debugging
                Logger.Debug("Procedures:");
                foreach (DataRow row in trustedAssembliesTable.Rows)
                {
                    string name = row["name"].ToString();
                    Logger.DebugNested(name);

                    // Check if the assemblyName is a substring of the description
                    if (name == procedureName)
                    {
                        Logger.Success($"Procedure '{procedureName}' exist");
                        return true;
                    }
                }

                Logger.Warning($"Procedure '{procedureName}' does not exist");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error retrieving procedures: {ex.Message}");
                return false;
            }
        }


        /// <summary>
        /// Enables or disables a specified SQL Server configuration option using sp_configure.
        /// </summary>
        /// <param name="optionName">The name of the configuration option to modify.</param>
        /// <param name="value">The value to set for the configuration option (e.g., 1 to enable, 0 to disable).</param>
        public bool SetConfigurationOption(string optionName, int value)
        {

            if (!EnableAdvancedOptions())
            {
                Logger.Error("Cannot proceed without 'show advanced options' enabled.");
                return false;
            }


            Logger.Task($"Checking status of '{optionName}'");
            try
            {
                // Check other module status via sys.configurations
                var configValue = _queryService.ExecuteScalar($"SELECT value FROM master.sys.configurations WHERE name = '{optionName}';");
                if (configValue == null)
                {
                    Logger.Warning($"Configuration '{optionName}' not found or inaccessible");
                    return false;
                }

                if (Convert.ToInt32(configValue) == value)
                {
                    Logger.Info($"Configuration option '{optionName}' is already set to {value}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error checking module status for '{optionName}': {ex.Message}");
                return false;
            }

            try
            {
                // Update the configuration option
                Logger.Info($"Updating configuration option '{optionName}' to {value}.");
                _queryService.ExecuteNonProcessing($"EXEC master..sp_configure '{optionName}', {value}; RECONFIGURE;");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to set configuration option '{optionName}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Adds a CLR assembly hash to the list of trusted assemblies in SQL Server.
        /// </summary>
        /// <param name="assemblyHash">The SHA-512 hash of the assembly to trust.</param>
        /// <param name="assemblyDescription">A description of the assembly (e.g., name, version, etc.).</param>
        /// <returns>True if the hash was successfully added, false otherwise.</returns>
        public bool RegisterTrustedAssembly(string assemblyHash, string assemblyDescription)
        {
            if (_server.Legacy)
            {
                Logger.Warning("CLR hash cannot be added to legacy servers");
                return false;
            }

            try
            {
                // Check if the hash already exists
                string checkHash = _queryService.ExecuteScalar($"SELECT * FROM master.sys.trusted_assemblies WHERE hash = 0x{assemblyHash};")?.ToString()?.ToLower();

                if (checkHash?.Contains("permission was denied") == true)
                {
                    Logger.Error("Insufficient privileges to perform this action");
                    return false;
                }

                if (checkHash?.Contains("system.byte[]") == true)
                {
                    Logger.Warning("Hash already exists in sys.trusted_assemblies");

                    // Attempt to remove the existing hash
                    string deletionQuery = _queryService.ExecuteScalar($"EXEC master..sp_drop_trusted_assembly 0x{assemblyHash};")?.ToString()?.ToLower();

                    if (deletionQuery?.Contains("permission was denied") == true)
                    {
                        Logger.Error("Insufficient privileges to remove existing trusted assembly");
                        return false;
                    }

                    Logger.Success("Existing hash removed successfully");
                }

                // Add the new hash to the trusted assemblies
                _queryService.ExecuteNonProcessing($@"
                    EXEC master..sp_add_trusted_assembly
                    0x{assemblyHash},
                    N'{assemblyDescription}, version=0.0.0.0, culture=neutral, publickeytoken=null, processorarchitecture=msil';
                ");

                // Verify if the hash was successfully added
                if (CheckTrustedAssembly(assemblyDescription))
                {
                    Logger.Success($"Added assembly hash '0x{assemblyHash}' as trusted");
                    return true;
                }

                Logger.Error("Failed to add hash to sys.trusted_assemblies");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"An error occurred while adding the CLR hash: {ex.Message}");
                return false;
            }
        }


        /// <summary>
        /// Ensures that 'show advanced options' is enabled.
        /// Returns true if successfully enabled or already enabled, otherwise false.
        /// </summary>
        private bool EnableAdvancedOptions()
        {
            Logger.Task("Ensuring advanced options are enabled");


            var advancedOptionsEnabled = _queryService.ExecuteScalar("SELECT value_in_use FROM master.sys.configurations WHERE name = 'show advanced options';");

            if (advancedOptionsEnabled != null && Convert.ToInt32(advancedOptionsEnabled) == 1)
            {
                Logger.Info("Advanced options already enabled");
                return true;
            }

            Logger.Info("Enabling advanced options...");

            string query = "EXEC master..sp_configure 'show advanced options', 1; RECONFIGURE;";

            try
            {
                _queryService.ExecuteNonProcessing(query);
            }
            catch (Exception)
            {
                return false;
            }
                    

            // Verify the change
            advancedOptionsEnabled = _queryService.ExecuteScalar("SELECT value_in_use FROM master.sys.configurations WHERE name = 'show advanced options';");

            if (advancedOptionsEnabled != null && Convert.ToInt32(advancedOptionsEnabled) == 1)
            {
                Logger.Success("Advanced options successfully enabled");
                return true;
            }

            Logger.Warning("Failed to verify 'show advanced options' was enabled");
            return false;

        }


        /// <summary>
        /// Enables data access for the SQL Server.
        /// </summary>
        public bool EnableDataAccess(string serverName)
        {
            Logger.Task($"Enabling data access on server '{serverName}'");
            try
            {
                string query = $"EXEC master..sp_serveroption '{serverName}', 'DATA ACCESS', TRUE;";
                _queryService.ExecuteNonProcessing(query);

                // Verify if data access is enabled
                if (IsDataAccessEnabled(serverName))
                {
                    Logger.Success($"Data access enabled for server '{serverName}'");
                    return true;
                }

                Logger.Error($"Failed to enable data access for server '{serverName}'");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error enabling data access for server '{serverName}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Disables data access for the SQL Server.
        /// </summary>
        public bool DisableDataAccess(string serverName)
        {
            Logger.Task($"Disabling data access on server '{serverName}'");
            try
            {
                string query = $"EXEC master..sp_serveroption '{serverName}', 'DATA ACCESS', FALSE;";
                _queryService.ExecuteNonProcessing(query);

                // Verify if data access is disabled
                if (!IsDataAccessEnabled(serverName))
                {
                    Logger.Success($"Data access disabled for server '{serverName}'");
                    return true;
                }

                Logger.Error($"Failed to disable data access for server '{serverName}'");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error disabling data access for server '{serverName}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if data access is enabled for a specified SQL Server.
        /// </summary>
        private bool IsDataAccessEnabled(string serverName)
        {
            Logger.Task($"Checking data access status for server '{serverName}'");
            try
            {
                string query = $"SELECT CAST(is_data_access_enabled AS INT) AS IsEnabled FROM master.sys.servers WHERE name = '{serverName}';";
                object result = _queryService.ExecuteScalar(query);

                if (result == null)
                {
                    Logger.Warning($"Server '{serverName}' not found in sys.servers");
                    return false;
                }

                return Convert.ToInt32(result) == 1;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error checking data access status for server '{serverName}': {ex.Message}");
                return false;
            }
        }

        public void DropDependentObjects(string assemblyName)
        {
            try
            {
                Logger.Task($"Identifying dependent objects for assembly '{assemblyName}'");

                string query = $@"
            SELECT o.type_desc, o.name 
            FROM master.sys.assembly_modules am
            JOIN master.sys.objects o ON am.object_id = o.object_id
            WHERE am.assembly_id = (
                SELECT assembly_id 
                FROM master.sys.assemblies 
                WHERE name = '{assemblyName}'
            );";

                DataTable dependencies = _queryService.ExecuteTable(query);

                if (dependencies.Rows.Count == 0)
                {
                    Logger.Info($"No dependent objects found for assembly '{assemblyName}'. Nothing to drop.");
                    return;
                }

                Logger.Info($"Found {dependencies.Rows.Count} dependent objects for assembly '{assemblyName}'.");

                foreach (DataRow row in dependencies.Rows)
                {
                    string objectType = row["type_desc"].ToString();
                    string objectName = row["name"].ToString();

                    // Map object types to DROP statements
                    string dropCommand;
                    switch (objectType)
                    {
                        case "CLR_SCALAR_FUNCTION":
                        case "SQL_SCALAR_FUNCTION":
                            dropCommand = $"DROP FUNCTION IF EXISTS [{objectName}];";
                            break;

                        case "CLR_TABLE_VALUED_FUNCTION":
                        case "SQL_TABLE_VALUED_FUNCTION":
                            dropCommand = $"DROP FUNCTION IF EXISTS [{objectName}];";
                            break;

                        case "CLR_STORED_PROCEDURE":
                        case "SQL_STORED_PROCEDURE":
                            dropCommand = $"DROP PROCEDURE IF EXISTS [{objectName}];";
                            break;

                        case "VIEW":
                            dropCommand = $"DROP VIEW IF EXISTS [{objectName}];";
                            break;

                        default:
                            Logger.Warning($"Unsupported object type '{objectType}' for object '{objectName}'. Skipping.");
                            continue;
                    }

                    Logger.Info($"Dropping dependent object '{objectName}' of type '{objectType}'");
                    _queryService.ExecuteNonProcessing(dropCommand);
                }

                Logger.Success($"All dependent objects for assembly '{assemblyName}' dropped successfully");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to drop dependent objects for assembly '{assemblyName}': {ex.Message}");
            }
        }
    }
}
