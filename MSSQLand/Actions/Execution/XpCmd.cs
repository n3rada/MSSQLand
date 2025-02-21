using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace MSSQLand.Actions.Execution
{
    internal class XpCmd : BaseAction
    {
        private string _command;

        /// <summary>
        /// Validates the arguments passed to the Shell action.
        /// </summary>
        /// <param name="additionalArguments">The command to execute using xp_cmdshell.</param>
        public override void ValidateArguments(string additionalArguments)
        {
            if (string.IsNullOrEmpty(additionalArguments))
            {
                throw new ArgumentException("Shell action requires a CMD command.");
            }

            _command = additionalArguments;
        }


        /// <summary>
        /// Executes the provided shell command on the SQL server using xp_cmdshell.
        /// </summary>
        /// <param name="databaseContext">The ConnectionManager instance to execute the query.</param>
        /// <returns>A list of strings containing the command output, or an empty list if no output.</returns>
        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Executing command: {_command}");

            string query = $"EXEC master..xp_cmdshell '{_command.Replace("'", "''")}'"; // Sanitize single quotes in the command

            // Enable 'xp_cmdshell'
            databaseContext.ConfigService.SetConfigurationOption("xp_cmdshell", 1);

            List<string> outputLines = new();

            using SqlDataReader result = databaseContext.QueryService.Execute(query);

            if (result.HasRows)
            {
                while (result.Read()) // Read each row in the result
                {
                    string output = result.IsDBNull(0) ? string.Empty : result.GetString(0); // Handle nulls gracefully
                    outputLines.Add(output);
                }

                return outputLines; // Return the collected output
            }

            Logger.Warning("The command executed but returned no results.");
            return outputLines; // Return empty list if no output
        }
    }
}
