using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSSQLand.Services
{
    public class ConfigurationService
    {
        private readonly QueryService _queryService;

        public ConfigurationService(QueryService queryService)
        {
            _queryService = queryService;
        }

        /// <summary>
        /// Enables or disables a specified SQL Server configuration option using sp_configure.
        /// </summary>
        /// <param name="optionName">The name of the configuration option to modify.</param>
        /// <param name="value">The value to set for the configuration option (e.g., 1 to enable, 0 to disable).</param>
        public void SetConfigurationOption(string optionName, int value)
        {
            try
            {
                // Check the current status of the configuration option
                var currentValue = _queryService.ExecuteScalar($"SELECT value_in_use FROM sys.configurations WHERE name = '{optionName}';");

                if (currentValue == null)
                {
                    Logger.Warning($"Configuration option '{optionName}' not found.");
                    return;
                }

                if (Convert.ToInt32(currentValue) == value)
                {
                    Logger.Info($"Configuration option '{optionName}' is already set to {value}.");
                    return;
                }

                // Update the configuration option
                Logger.Info($"Updating configuration option '{optionName}' to {value}.");
                _queryService.ExecuteNonProcessing($"EXEC sp_configure '{optionName}', {value}; RECONFIGURE;");
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to set configuration option '{optionName}': {ex.Message}");
            }
        }


        /// <summary>
        /// Checks the status of a specific module or configuration on the target server.
        /// </summary>
        /// <param name="moduleName">The name of the module or configuration to check.</param>
        /// <param name="targetServer">The target linked server for RPC checks.</param>
        /// <returns>True if the module is enabled, otherwise false.</returns>
        public bool CheckModuleStatus(string moduleName, string targetServer)
        {
            Logger.Task($"Checking status of '{moduleName}' on {targetServer}");

            try
            {
                // Handle RPC module check
                if (moduleName.Equals("rpc", StringComparison.OrdinalIgnoreCase))
                {
                    var rpcStatus = _queryService.ExecuteScalar($"SELECT is_rpc_out_enabled FROM sys.servers WHERE LOWER(name) = LOWER('{targetServer}');");
                    if (rpcStatus == null)
                    {
                        Logger.Warning($"RPC status could not be determined for server {targetServer}.");
                        return false;
                    }
                    return Convert.ToBoolean(rpcStatus);
                }

                // Ensure advanced options are enabled for configuration queries
                EnsureAdvancedOptions();

                // Check other module status via sys.configurations
                var configValue = _queryService.ExecuteScalar($"SELECT value FROM sys.configurations WHERE name = '{moduleName}';");
                if (configValue == null)
                {
                    Logger.Warning($"Configuration '{moduleName}' not found or inaccessible.");
                    return false;
                }
                return Convert.ToInt32(configValue) == 1;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error checking module status for '{moduleName}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Ensures that advanced options are enabled for accessing certain configurations.
        /// </summary>
        public void EnsureAdvancedOptions()
        {
            try
            {
                var advancedOptionsEnabled = _queryService.ExecuteScalar("SELECT value_in_use FROM sys.configurations WHERE name = 'show advanced options';");


                if (advancedOptionsEnabled == null || Convert.ToInt32(advancedOptionsEnabled) != 1)
                {
                    // Enable 'show advanced options'
                    SetConfigurationOption("show advanced options", 1);
                }
                else
                {
                    Logger.Info("Adanced options already on");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to verify or enable 'show advanced options': {ex.Message}");
            }
        }
    }
}
