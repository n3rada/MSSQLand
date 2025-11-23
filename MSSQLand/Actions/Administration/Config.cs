using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Collections.Generic;
using System.Data;

namespace MSSQLand.Actions.Administration
{
    internal class Config : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "o", LongName = "option", Required = false, Description = "Configuration option name (omit to list all)")]
        private string _optionName = null;

        [ArgumentMetadata(Position = 1, ShortName = "v", LongName = "value", Required = false, Description = "Value to set (0=disable, 1=enable)")]
        private int _value = -1;

        public override void ValidateArguments(string[] args)
        {
            // Automatic binding handles positional and named args
            BindArgumentsToFields(args);

            // No further action required; _optionName may be null to list all options
            if (_value < -1)
            {
                throw new ArgumentException("Invalid value for configuration option");
            }

            if (!string.IsNullOrEmpty(_optionName) && _value >= 0 && _value != 0 && _value != 1)
            {
                throw new ArgumentException("Invalid value. Use 1 to enable or 0 to disable.");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            // Mode 1: Set configuration option
            if (_value >= 0 && !string.IsNullOrEmpty(_optionName))
            {
                Logger.TaskNested($"Setting {_optionName} to {_value}");
                databaseContext.ConfigService.SetConfigurationOption(_optionName, _value);
                return null;
            }

            // Mode 2: Show specific option status
            if (!string.IsNullOrEmpty(_optionName) && _value < 0)
            {
                Logger.Task($"Checking status of '{_optionName}'");
                int status = databaseContext.ConfigService.GetConfigurationStatus(_optionName);
                
                if (status < 0)
                {
                    Logger.Warning($"Configuration '{_optionName}' not found or inaccessible");
                    return null;
                }

                Logger.Info($"{_optionName}: {(status == 1 ? "Enabled" : "Disabled")}");
                return status;
            }

            // Mode 3: List all security-sensitive configurations
            Logger.Task("Listing all configuration options");
            var results = CheckConfigurationOptions(databaseContext);

            if (results.Count > 0)
            {
                Logger.NewLine();
                DisplayResults(results);
                return results;
            }

            Logger.Warning("No configuration information could be retrieved");
            return null;
        }

        /// <summary>
        /// Checks security-sensitive configuration options.
        /// </summary>
        private List<Dictionary<string, object>> CheckConfigurationOptions(DatabaseContext databaseContext)
        {
            var results = new List<Dictionary<string, object>>();

            try
            {
                // Fetch all configurations at once
                string query = "SELECT name, value_in_use FROM sys.configurations ORDER BY name;";
                DataTable configsTable = databaseContext.QueryService.ExecuteTable(query);
                
                foreach (DataRow row in configsTable.Rows)
                {
                    string name = row["name"].ToString();
                    int status = Convert.ToInt32(row["value_in_use"]);
                    
                    results.Add(new Dictionary<string, object>
                    {
                        { "Feature", name },
                        { "Activated", status == 1 ? "True" : "False" },
                        { "Value", status }
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Could not retrieve configuration options: {ex.Message}");
            }

            return results;
        }

        /// <summary>
        /// Displays the results in a formatted table.
        /// </summary>
        private void DisplayResults(List<Dictionary<string, object>> results)
        {
            // Convert to DataTable for formatting
            DataTable dt = new();
            dt.Columns.Add("Option", typeof(string));
            dt.Columns.Add("Value", typeof(string));
            dt.Columns.Add("Enabled", typeof(string));

            foreach (var result in results)
            {
                dt.Rows.Add(
                    result["Feature"],
                    result["Value"],
                    result["Activated"]
                );
            }

            Console.WriteLine(OutputFormatter.ConvertDataTable(dt));
        }
    }
}
