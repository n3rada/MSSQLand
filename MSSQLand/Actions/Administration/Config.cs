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

            // Split the additional argument into parts
            string[] parts = SplitArguments(additionalArguments);

            // One argument = list specific option
            if (parts.Length == 1)
            {
                _optionName = parts[0];
                _value = -1;
                return;
            }

            // Two arguments = set option value
            if (parts.Length == 2)
            {
                _optionName = parts[0];

                // Validate and parse the value (1 or 0)
                if (!int.TryParse(parts[1], out _value) || (_value != 0 && _value != 1))
                {
                    throw new ArgumentException("Invalid value. Use 1 to enable or 0 to disable.");
                }
                return;
            }

            throw new ArgumentException("Invalid arguments. Usage: [option] [value]\n  No args: list all\n  One arg: show status of option\n  Two args: set option value (0 or 1)");
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
            Logger.Task("Checking security-sensitive configuration options");
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

            // List of security-sensitive configuration options with descriptions
            var configOptions = new Dictionary<string, string>
            {
                { "xp_cmdshell", "Execute operating system commands" },
                { "Ole Automation Procedures", "Use OLE Automation objects" },
                { "clr enabled", "Execute CLR assemblies" },
                { "Agent XPs", "SQL Server Agent extended procedures" },
                { "Ad Hoc Distributed Queries", "OPENROWSET/OPENDATASOURCE queries" },
                { "show advanced options", "Access advanced configuration options" },
                { "remote access", "Allow remote server access" },
                { "remote admin connections", "Dedicated Admin Connection (DAC)" },
                { "Database Mail XPs", "Send emails via Database Mail" },
                { "SMO and DMO XPs", "SQL Server Management Objects" }
            };

            try
            {
                foreach (var option in configOptions)
                {
                    int status = databaseContext.ConfigService.GetConfigurationStatus(option.Key);
                    
                    if (status >= 0)
                    {
                        results.Add(new Dictionary<string, object>
                        {
                            { "Feature", option.Key },
                            { "Type", "Configuration" },
                            { "Activated", status == 1 ? "True" : "False" },
                            { "Description", option.Value }
                        });
                    }
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
            dt.Columns.Add("Activated", typeof(string));
            dt.Columns.Add("Description", typeof(string));

            foreach (var result in results)
            {
                dt.Rows.Add(
                    result["Feature"],
                    result["Activated"],
                    result["Description"]
                );
            }

            // Display as markdown table
            Console.WriteLine(OutputFormatter.ConvertDataTable(dt));

            // Summary
            Logger.NewLine();
            int activatedCount = results.FindAll(r => r["Activated"].ToString() == "True").Count;
            Logger.Info($"Total: {results.Count} configuration options checked, {activatedCount} enabled");
        }
    }
}
