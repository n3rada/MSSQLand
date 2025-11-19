using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Data;

namespace MSSQLand.Actions.Database
{
    /// <summary>
    /// Checks and displays security-sensitive SQL Server configuration options.
    /// 
    /// This action enumerates various server-level configuration options that impact security,
    /// showing which are currently enabled or disabled. These options control features like
    /// command execution (xp_cmdshell), OLE automation, CLR assemblies, and more.
    /// 
    /// Note: This checks server-wide configuration options. For user/role permissions,
    /// use the 'permissions' action instead.
    /// </summary>
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
            Logger.Info("Checking server-wide security-sensitive configuration options");
            Logger.InfoNested("Note: Use 'permissions' action to see user/role-level permissions");
            Logger.NewLine();

            var results = CheckConfigurationOptions(databaseContext);

            // Display results
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
                            { "Enabled", status == 1 ? "Yes" : "No" },
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
            // Separate enabled and disabled options
            var enabledOptions = new List<Dictionary<string, object>>();
            var disabledOptions = new List<Dictionary<string, object>>();

            foreach (var result in results)
            {
                if (result["Enabled"].ToString() == "Yes")
                {
                    enabledOptions.Add(result);
                }
                else
                {
                    disabledOptions.Add(result);
                }
            }

            // Display enabled options first (security concern)
            if (enabledOptions.Count > 0)
            {
                Logger.Warning($"Enabled Configuration Options ({enabledOptions.Count})");
                DataTable enabledTable = new();
                enabledTable.Columns.Add("Option", typeof(string));
                enabledTable.Columns.Add("Description", typeof(string));

                foreach (var result in enabledOptions)
                {
                    enabledTable.Rows.Add(
                        result["Feature"],
                        result["Description"]
                    );
                }

                Console.WriteLine(OutputFormatter.ConvertDataTable(enabledTable));
                Logger.NewLine();
            }

            // Display disabled options
            if (disabledOptions.Count > 0)
            {
                Logger.Success($"Disabled Configuration Options ({disabledOptions.Count})");
                DataTable disabledTable = new();
                disabledTable.Columns.Add("Option", typeof(string));
                disabledTable.Columns.Add("Description", typeof(string));

                foreach (var result in disabledOptions)
                {
                    disabledTable.Rows.Add(
                        result["Feature"],
                        result["Description"]
                    );
                }

                Console.WriteLine(OutputFormatter.ConvertDataTable(disabledTable));
                Logger.NewLine();
            }
        }
    }
}
