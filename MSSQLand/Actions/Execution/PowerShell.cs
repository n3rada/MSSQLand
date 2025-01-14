using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Text;

namespace MSSQLand.Actions
{
    internal class PowerShell : XpCmd
    {
        private string _script;

        /// <summary>
        /// Validates the arguments passed to the PowerShell action.
        /// </summary>
        /// <param name="additionalArgument">The PowerShell script to execute.</param>
        public override void ValidateArguments(string additionalArgument)
        {
            if (string.IsNullOrEmpty(additionalArgument))
            {
                throw new ArgumentException("PowerShell action requires a script to execute.");
            }

            _script = additionalArgument;
        }

        /// <summary>
        /// Executes the provided PowerShell script on the SQL server using xp_cmdshell.
        /// </summary>
        /// <param name="connectionManager">The ConnectionManager instance to execute the query.</param>
        public override void Execute(DatabaseContext connectionManager)
        {
            Logger.TaskNested($"Executing PowerShell script: {_script}");

            // Convert the PowerShell script to Base64 encoding
            string base64EncodedScript = ConvertToBase64(_script);

            // Craft the PowerShell command to execute the Base64-encoded script
            string powerShellCommand = $"powershell.exe -noni -NoLogo -e {base64EncodedScript}";

            // Set the crafted PowerShell command as the _command in the parent class
            base.ValidateArguments(powerShellCommand);

            // Call the parent's Execute method to execute the command
            base.Execute(connectionManager);
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
