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
        private string _optionName;

        [ArgumentMetadata(Position = 1, ShortName = "v", LongName = "value", Required = false, Description = "Value to set (0=disable, 1=enable)")]
        private int _value = -1;

        public override void ValidateArguments(string additionalArguments)
        {
            // No arguments = list all configurations
            if (string.IsNullOrWhiteSpace(additionalArguments))
            {
                _optionName = null;
                _value = -1;
                return;
            }

            // Parse both positional and named arguments
            var (namedArgs, positionalArgs) = ParseArguments(additionalArguments);

            // Get option name from position 0 or /o: or /option:
            _optionName = GetNamedArgument(namedArgs, "o")
                       ?? GetNamedArgument(namedArgs, "option")
                       ?? GetPositionalArgument(positionalArgs, 0);

            // Get value from position 1 or /v: or /value:
            string valueStr = GetNamedArgument(namedArgs, "v")
                           ?? GetNamedArgument(namedArgs, "value")
                           ?? GetPositionalArgument(positionalArgs, 1);

            if (!string.IsNullOrEmpty(valueStr))
            {
                // Validate and parse the value (1 or 0)
                if (!int.TryParse(valueStr, out _value) || (_value != 0 && _value != 1))
                {
                    throw new ArgumentException("Invalid value. Use 1 to enable or 0 to disable.");
                }
            }
            else
            {
                _value = -1; // No value specified = list mode
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
