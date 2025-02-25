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

            // Ensure 'xp_cmdshell' is enabled
            if (!databaseContext.ConfigService.SetConfigurationOption("xp_cmdshell", 1))
            {
                Logger.Error("Failed to enable 'xp_cmdshell'. Cannot proceed with command execution.");
                return null;
            }

            // Sanitize command to prevent SQL injection issues
            string query = $"EXEC master..xp_cmdshell '{_command.Replace("'", "''")}'";


            List<string> outputLines = new();

            try
            {
                using SqlDataReader result = databaseContext.QueryService.Execute(query);

                if (result.HasRows)
                {
                    Logger.NewLine();
                    while (result.Read())
                    {
                        string output = result.IsDBNull(0) ? string.Empty : result.GetString(0);
                        Console.WriteLine(output);
                        outputLines.Add(output);
                    }

                    return outputLines;
                }

                Logger.Warning("The command executed but returned no results.");
                return outputLines;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error executing xp_cmdshell: {ex.Message}");
                return null;
            }
        }
    }
}
