using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;
using System.Linq;

namespace MSSQLand.Actions.Administration
{
    internal class Kill : BaseAction
    {
        private string _target;

        public override void ValidateArguments(string additionalArguments)
        {
            if (string.IsNullOrEmpty(additionalArguments))
            {
                throw new ArgumentException("Please specify a session ID or 'all' as an argument.");
            }

            _target = additionalArguments.Trim();

            // Verify target is "all" or a valid integer
            if (_target.ToLower() != "all" && (!Int16.TryParse(_target, out Int16 sessionId) || sessionId <= 0))
            {
                throw new ArgumentException("Invalid argument. Provide a positive session ID or 'all'.");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.NewLine();
            Logger.Info($"Preparing to kill session(s) for target: {_target}");

            string allSessionsQuery = @"
            SELECT 
                r.session_id AS SessionID,
                r.request_id AS RequestID,
                r.start_time AS StartTime,
                r.status AS Status,
                r.command AS Command,
                DB_NAME(r.database_id) AS DatabaseName,
                r.wait_type AS WaitType,
                r.wait_time AS WaitTime,
                r.blocking_session_id AS BlockingSessionID,
                t.text AS SQLText,
                c.client_net_address AS ClientAddress,
                c.connect_time AS ConnectionStart
            FROM sys.dm_exec_requests r
            CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t
            LEFT JOIN sys.dm_exec_connections c
                ON r.session_id = c.session_id
            WHERE r.session_id != @@SPID
            ORDER BY r.start_time DESC;";

            try
            {
                // Fetch all running sessions
                Logger.Task("Fetching all running sessions...");
                DataTable sessionsTable = databaseContext.QueryService.ExecuteTable(allSessionsQuery);

                if (sessionsTable == null || sessionsTable.Rows.Count == 0)
                {
                    Logger.Warning("No running sessions found.");
                    return true;
                }

                // If specific session ID is provided, validate and kill
                if (_target.ToLower() != "all")
                {
                    Int16 targetSessionId = Int16.Parse(_target); // Assumes validation is done earlier
                    DataRow foundSession = sessionsTable.AsEnumerable()
                        .FirstOrDefault(row => row.Field<int>("SessionID") == targetSessionId);

                    if (foundSession == null)
                    {
                        Logger.Warning($"Session {_target} not found or not valid.");
                        return false;
                    }

                    // Kill the specific session
                    Logger.Task($"Killing session {_target}...");
                    databaseContext.QueryService.ExecuteNonProcessing($"KILL {targetSessionId};");
                    Logger.Success($"Session {_target} killed successfully.");
                    return true;
                }

                // If "all" is specified, loop through all sessions and kill them
                Logger.Task("Killing all sessions...");
                foreach (DataRow row in sessionsTable.Rows)
                {
                    Int16 sessionId = row.Field<Int16>("SessionID");
                    Logger.Info($"Killing session {sessionId}...");
                    databaseContext.QueryService.ExecuteNonProcessing($"KILL {sessionId};");
                }

                Logger.Success("All sessions killed successfully.");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"An error occurred while processing: {ex.Message}");
                return false;
            }
        }
    }
}
