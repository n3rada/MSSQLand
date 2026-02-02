// MSSQLand/Actions/Administration/Config.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace MSSQLand.Actions.Administration
{
    internal class Config : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "o", LongName = "option", Required = false, Description = "Configuration option name (omit to list all)")]
        private string _optionName = null;

        [ArgumentMetadata(Position = 1, ShortName = "v", LongName = "value", Required = false, Description = "Value to set (0/1 or enable/disable)")]
        private string _valueRaw = null;

        [ExcludeFromArguments]
        private int _value = -1;

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);

            if (string.IsNullOrWhiteSpace(_valueRaw))
            {
                _value = -1;
            }
            else if (int.TryParse(_valueRaw, out int parsedValue))
            {
                _value = parsedValue;
            }
            else if (TryParseToggleAction(_valueRaw, out bool enabled, out string error))
            {
                _value = enabled ? 1 : 0;
            }
            else
            {
                throw new ArgumentException(error);
            }

            // Validation
            if (_value < -1)
            {
                throw new ArgumentException("Invalid value for configuration option");
            }

            if (!string.IsNullOrEmpty(_optionName) && _value >= 0 && _value != 0 && _value != 1)
            {
                throw new ArgumentException("Invalid value. Use 1 to enable or 0 to disable.");
            }
        }

        public override object Execute(DatabaseContext databaseContext)
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
                Logger.TaskNested($"Checking status of '{_optionName}'");
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
            Logger.TaskNested("Listing all configuration options");
            var results = CheckConfigurationOptions(databaseContext);

            if (results.Count > 0)
            {
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
                // Check if user can modify configurations (requires ALTER SETTINGS or sysadmin)
                bool canModify = databaseContext.UserService.IsAdmin();
                if (!canModify)
                {
                    try
                    {
                        string permQuery = "SELECT HAS_PERMS_BY_NAME(NULL, NULL, 'ALTER SETTINGS') AS HasPerm;";
                        var permResult = databaseContext.QueryService.ExecuteTable(permQuery);
                        if (permResult.Rows.Count > 0)
                        {
                            canModify = Convert.ToInt32(permResult.Rows[0]["HasPerm"]) == 1;
                        }
                    }
                    catch
                    {
                        // Silently ignore permission check errors
                    }
                }

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
                        { "Value", status },
                        { "CanModify", canModify }
                    });
                }

                // Log permission status
                if (canModify)
                {
                    Logger.SuccessNested("You have ALTER SETTINGS permission (can modify configurations)");
                }
                else
                {
                    Logger.WarningNested("You cannot modify configurations (requires sysadmin or ALTER SETTINGS)");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Could not retrieve configuration options: {ex.Message}");
            }

            return results;
        }

        /// <summary>
        /// Displays the results in a formatted table, ordered by enabled status first.
        /// </summary>
        private void DisplayResults(List<Dictionary<string, object>> results)
        {
            // Convert to DataTable for formatting
            DataTable dt = new();
            dt.Columns.Add("Option", typeof(string));
            dt.Columns.Add("Value", typeof(string));
            dt.Columns.Add("Enabled", typeof(string));

            // Sort: enabled first, then alphabetically by option name
            var sortedResults = results
                .OrderByDescending(r => r["Activated"].ToString() == "True")
                .ThenBy(r => r["Feature"].ToString());

            foreach (var result in sortedResults)
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
