// MSSQLand/Actions/Agent/AgentHelper.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;

namespace MSSQLand.Actions.Agent
{
    /// <summary>
    /// Shared helper methods for SQL Server Agent actions.
    /// </summary>
    internal static class AgentHelper
    {
        /// <summary>
        /// Checks if SQL Server Agent is running using sys.dm_exec_sessions.
        /// </summary>
        public static bool CheckAgentRunning(DatabaseContext databaseContext)
        {
            try
            {
                string query = @"
                    SELECT CASE
                        WHEN EXISTS (
                            SELECT 1 FROM sys.dm_exec_sessions
                            WHERE program_name LIKE 'SQLAgent%'
                        ) THEN 'Running'
                        ELSE 'Stopped'
                    END AS AgentStatus;";

                DataTable result = databaseContext.QueryService.ExecuteTable(query);
                string status = result.Rows[0]["AgentStatus"].ToString();

                if (status == "Running")
                {
                    Logger.Success("SQL Server Agent is running");
                    return true;
                }

                Logger.Error("SQL Server Agent is not running");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to check Agent status: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Translates run_status int to human-readable string.
        /// </summary>
        public static string RunStatusToString(int runStatus)
        {
            return runStatus switch
            {
                0 => "Failed",
                1 => "Succeeded",
                2 => "Retry",
                3 => "Cancelled",
                4 => "Running",
                5 => "Unknown",
                _ => runStatus.ToString()
            };
        }

        /// <summary>
        /// Formats run_duration (hhmmss int) to a readable string.
        /// </summary>
        public static string FormatDuration(int duration)
        {
            int hours = duration / 10000;
            int minutes = (duration % 10000) / 100;
            int seconds = duration % 100;
            return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
        }
    }
}
