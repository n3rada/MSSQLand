using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Data;

namespace MSSQLand.Actions.Database
{
    internal class Configs : BaseAction
    {
        /// <summary>
        /// Validates the arguments passed to the Configs action.
        /// </summary>
        public override void ValidateArguments(string additionalArguments)
        {
            // No additional arguments required
        }

        /// <summary>
        /// Lists security-sensitive configuration options with their activation status.
        /// </summary>
        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.Task("Checking security-sensitive configuration options");

            var results = CheckConfigurationOptions(databaseContext);

            // Display results
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
