using MSSQLand.Utilities;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Linq;


namespace MSSQLand.Services
{
    public class ConfigurationService
    {
        private readonly QueryService _queryService;

        public ConfigurationService(QueryService queryService)
        {
            _queryService = queryService;
        }

        

        public bool CheckAssembly(string assemblyName)
        {
            string query = $"SELECT name FROM sys.assemblies WHERE name='{assemblyName}';";

            return _queryService.ExecuteScalar(query)?.ToString() == assemblyName;
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

            EnableAdvancedOptions();

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
                _queryService.ExecuteNonProcessing($"EXEC sp_configure '{optionName}', {value}; RECONFIGURE;");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to set configuration option '{optionName}': {ex.Message}");
                return false;
            }
        }


        /// <summary>
        /// Ensures that 'show advanced options' is enabled.
        /// </summary>
        private void EnableAdvancedOptions()
        {
            Logger.Task("Ensuring advanced options are enabled");
            var advancedOptionsEnabled = _queryService.ExecuteScalar("SELECT value_in_use FROM sys.configurations WHERE name = 'show advanced options';");

            if (advancedOptionsEnabled == null || Convert.ToInt32(advancedOptionsEnabled) != 1)
            {
                try
                {
                    Logger.Info("Enabling advanced options");
                    _queryService.ExecuteNonProcessing("EXEC sp_configure 'show advanced options', 1; RECONFIGURE;");
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to enable 'show advanced options': {ex.Message}");
                }
            }
            else
            {
                Logger.Info("Advanced options already enabled");
            }
        }
    }
}
