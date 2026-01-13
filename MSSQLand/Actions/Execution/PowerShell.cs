// MSSQLand/Actions/Execution/PowerShell.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Text;

namespace MSSQLand.Actions.Execution
{
    internal class PowerShell : XpCmd
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "PowerShell script or command to execute")]
        private string _script;

        /// <summary>
        /// Validates the arguments passed to the PowerShell action.
        /// </summary>
        /// <param name="args">The PowerShell script to execute.</param>
        public override void ValidateArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                throw new ArgumentException("PowerShell action requires a script to execute.");
            }

            _script = string.Join(" ", args);
        }

        /// <summary>
        /// Executes the provided PowerShell script on the SQL server.
        /// </summary>
        /// <param name="databaseContext">The ConnectionManager instance to execute the query.</param>
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Executing PowerShell script: {_script}");

            // Convert the PowerShell script to Base64 encoding
            string base64EncodedScript = ConvertToBase64(_script);

            // Craft the PowerShell command to execute the Base64-encoded script
            string powerShellCommand = $"powershell.exe -noni -NoLogo -e {base64EncodedScript}";

            // Set the crafted PowerShell command as the _command in the parent class
            base.ValidateArguments(new string[] { powerShellCommand });

            // Call the parent's Execute method to execute the command
            return base.Execute(databaseContext);
        }

        /// <summary>
        /// Converts a string to Base64 encoding.
        /// </summary>
        /// <param name="input">The input string to encode.</param>
        /// <returns>The Base64-encoded string.</returns>
        private string ConvertToBase64(string input)
        {
            byte[] inputBytes = Encoding.Unicode.GetBytes(input); // PowerShell expects UTF-16 (Unicode) encoding
            return Convert.ToBase64String(inputBytes);
        }
    }
}
