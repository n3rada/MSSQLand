using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;
using System.Data.SqlClient;

namespace MSSQLand.Actions.Execution
{
    internal class XpCmd : BaseAction
    {
        private string _command;

        /// <summary>
        /// Validates the arguments passed to the Shell action.
        /// </summary>
        /// <param name="additionalArgument">The command to execute using xp_cmdshell.</param>
        public override void ValidateArguments(string additionalArgument)
        {
            if (string.IsNullOrEmpty(additionalArgument))
            {
                throw new ArgumentException("Shell action requires a CMD command.");
            }

            _command = additionalArgument;
        }

        /// <summary>
        /// Executes the provided shell command on the SQL server using xp_cmdshell.
        /// </summary>
        /// <param name="connectionManager">The ConnectionManager instance to execute the query.</param>
        public override void Execute(DatabaseContext connectionManager)
        {
            Logger.TaskNested($"Executing command: {_command}");

            string query = $"EXEC master..xp_cmdshell '{_command.Replace("'", "''")}'"; // Sanitize single quotes in the command

            connectionManager.ConfigService.EnsureAdvancedOptions();
            // Enable 'xp_cmdshell'
            connectionManager.ConfigService.SetConfigurationOption("xp_cmdshell", 1);

            using SqlDataReader result = connectionManager.QueryService.Execute(query);

            if (result.HasRows)
            {
                while (result.Read()) // Read each row in the result
                {
                    string output = result.IsDBNull(0) ? string.Empty : result.GetString(0); // Handle nulls gracefully
                    Console.WriteLine(output);
                }
            }
            else
            {
                Logger.Warning("The command executed but returned no results.");
            }
        }

    }
}
