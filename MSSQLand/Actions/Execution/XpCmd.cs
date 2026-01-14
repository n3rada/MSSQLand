// MSSQLand/Actions/Execution/XpCmd.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace MSSQLand.Actions.Execution
{
    internal class XpCmd : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Operating system command to execute")]
        private string _command = "";

        /// <summary>
        /// Validates the arguments passed to the Shell action.
        /// </summary>
        /// <param name="args">The command to execute.</param>
        public override void ValidateArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                throw new ArgumentException("Shell action requires a CMD command.");
            }

            _command = string.Join(" ", args);
        }

        /// <summary>
        /// Executes the provided shell command on the SQL server.
        /// </summary>
        /// <param name="databaseContext">The ConnectionManager instance to execute the query.</param>
        /// <returns>A list of strings containing the command output, or an empty list if no output.</returns>
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Executing command: {_command}");

            // Ensure 'xp_cmdshell' is enabled
            if (!databaseContext.ConfigService.SetConfigurationOption("xp_cmdshell", 1))
            {
                Logger.Error("Failed to enable 'xp_cmdshell'.");
                return null;
            }

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
            catch (SqlException ex)
            {
                // Handle specific xp_cmdshell proxy account error
                if (ex.Message.Contains("xp_cmdshell_proxy_account") || ex.Message.Contains("proxy account"))
                {
                    Logger.Error("xp_cmdshell proxy account is not configured or invalid.");
                    Logger.ErrorNested("1. SQL Server service account lacks permissions to execute the command");
                    Logger.ErrorNested("2. No xp_cmdshell proxy credential is configured");
                }
                else
                {
                    Logger.Error($"SQL Error executing xp_cmdshell: {ex.Message}");
                }
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error executing xp_cmdshell: {ex.Message}");
                return null;
            }
        }
    }
}
