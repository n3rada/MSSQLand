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

            EnableAdvancedOptions();

            Logger.Task($"Checking status of '{optionName}'");
            try
            {
                // Check other module status via sys.configurations
                var configValue = _queryService.ExecuteScalar($"SELECT value FROM sys.configurations WHERE name = '{optionName}';");
                if (configValue == null)
                {
                    Logger.Warning($"Configuration '{optionName}' not found or inaccessible.");
                    return;
                }

                if (Convert.ToInt32(configValue) == value)
                {
                    Logger.Info($"Configuration option '{optionName}' is already set to {value}.");
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error checking module status for '{optionName}': {ex.Message}");
                return;
            }

            try
            {
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
                    Logger.Info("Enabling advanced options...");
                    _queryService.ExecuteNonProcessing("EXEC sp_configure 'show advanced options', 1; RECONFIGURE;");
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to enable 'show advanced options': {ex.Message}");
                }
            }
            else
            {
                Logger.Info("Advanced options already enabled.");
            }
        }
    }
}
