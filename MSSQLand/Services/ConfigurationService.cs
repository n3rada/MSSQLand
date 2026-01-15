using MSSQLand.Models;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using MSSQLand.Exceptions;
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

        /// <summary>
        /// Gets the current status of a configuration option without logging.
        /// </summary>
        /// <param name="optionName">The name of the configuration option.</param>
        /// <returns>1 if enabled, 0 if disabled, -1 if not found or error.</returns>
        public int GetConfigurationStatus(string optionName)
        {
            try
            {
                var result = _queryService.ExecuteScalar($"SELECT value_in_use FROM sys.configurations WHERE name = '{optionName}';");
                return result != null ? Convert.ToInt32(result) : -1;
            }
            catch
            {
                return -1;
            }
        }

        public bool CheckAssembly(string assemblyName)
        {
            string query = $"SELECT name FROM sys.assemblies WHERE name='{assemblyName}';";

            return _queryService.ExecuteScalar(query)?.ToString() == assemblyName;
        }

        public bool CheckAssemblyModules(string assemblyName)
        {
            string query = $"SELECT * FROM sys.assembly_modules;";

            string result = OutputFormatter.ConvertDataTable(_queryService.ExecuteTable(query)).ToLower();

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
                string query = "SELECT description FROM sys.trusted_assemblies;";
                DataTable trustedAssembliesTable = _queryService.ExecuteTable(query);

                if (trustedAssembliesTable.Rows.Count == 0)
                {
                    Logger.Warning("No trusted assemblies found");
                    return false;
                }

                // Log all trusted assemblies for debugging
                Logger.Debug("Trusted assemblies");
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
                Logger.Debug("Procedures");
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
                var configValue = _queryService.ExecuteScalar($"SELECT value FROM sys.configurations WHERE name = '{optionName}';");
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
            if (_server.IsLegacy)
            {
                Logger.Warning("CLR hash cannot be added to legacy servers");
                return false;
            }

            try
            {
                // Check if the hash already exists
                string checkHash = _queryService.ExecuteScalar($"SELECT * FROM sys.trusted_assemblies WHERE hash = 0x{assemblyHash};")?.ToString()?.ToLower();

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

            var advancedOptionsEnabled = _queryService.ExecuteScalar("SELECT value_in_use FROM sys.configurations WHERE name = 'show advanced options';");

            if (advancedOptionsEnabled != null && Convert.ToInt32(advancedOptionsEnabled) == 1)
            {
                Logger.Info("Advanced options already enabled");
                return true;
            }

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
            advancedOptionsEnabled = _queryService.ExecuteScalar("SELECT value_in_use FROM sys.configurations WHERE name = 'show advanced options';");

            if (advancedOptionsEnabled != null && Convert.ToInt32(advancedOptionsEnabled) == 1)
            {
                Logger.Success("Advanced options successfully enabled");
                return true;
            }

            Logger.Warning("Failed to verify 'show advanced options' was enabled");
            return false;

        }


        /// <summary>
        /// Sets a server option using sp_serveroption.
        /// This is a generic method for configuring linked server options.
        /// </summary>
        /// <param name="serverName">The name of the linked server.</param>
        /// <param name="optionName">The option name (e.g., 'data access', 'rpc out', 'collation compatible').</param>
        /// <param name="optionValue">The option value ('true' or 'false').</param>
        /// <returns>True if the option was successfully set, false otherwise.</returns>
        public bool SetServerOption(string serverName, string optionName, string optionValue)
        {
            Logger.Task($"Setting '{optionName}' to '{optionValue}' on server '{serverName}'");
            try
            {
                string query = $@"
                    EXEC master..sp_serveroption 
                         @server = '{serverName}', 
                         @optname = '{optionName}', 
                         @optvalue = '{optionValue}';
                ";
                _queryService.ExecuteNonProcessing(query);
                Logger.Success($"Successfully set '{optionName}' to '{optionValue}' on '{serverName}'");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting '{optionName}' on server '{serverName}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Drops all dependent database objects (procedures, functions, views) associated with a CLR assembly.
        /// 
        /// This method identifies and removes objects that depend on the specified assembly before the assembly
        /// itself can be dropped. It queries sys.assembly_modules to find all dependent objects and drops them
        /// in the correct order.
        /// </summary>
        /// <param name="assemblyName">The name of the CLR assembly whose dependent objects should be dropped.</param>
        /// <remarks>
        /// Supported dependent object types:
        /// - CLR_SCALAR_FUNCTION / SQL_SCALAR_FUNCTION
        /// - CLR_TABLE_VALUED_FUNCTION / SQL_TABLE_VALUED_FUNCTION
        /// - CLR_STORED_PROCEDURE / SQL_STORED_PROCEDURE
        /// - VIEW
        /// 
        /// Objects of unsupported types will be skipped with a warning.
        /// All exceptions are caught and logged internally.
        /// </remarks>
        public void DropDependentObjects(string assemblyName)
        {
            try
            {
                Logger.Task($"Identifying dependent objects for assembly '{assemblyName}'");

                string query = $@"
            SELECT o.type_desc, o.name 
            FROM sys.assembly_modules am
            JOIN sys.objects o ON am.object_id = o.object_id
            WHERE am.assembly_id = (
                SELECT assembly_id 
                FROM sys.assemblies 
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
