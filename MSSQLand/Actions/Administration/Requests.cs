// MSSQLand/Actions/Administration/Requests.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;

namespace MSSQLand.Actions.Administration
{
    internal class Requests : BaseAction
    {
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Retrieving currently executing SQL requests");

            string currentCommandsQuery = @"
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
            FROM master.sys.dm_exec_requests r
            CROSS APPLY master.sys.dm_exec_sql_text(r.sql_handle) t
            LEFT JOIN master.sys.dm_exec_connections c
                ON r.session_id = c.session_id
            WHERE r.session_id != @@SPID
            ORDER BY r.start_time DESC;";

            var result = databaseContext.QueryService.Execute(currentCommandsQuery);
            Console.WriteLine(OutputFormatter.ConvertSqlDataReader(result));
            return null;
        }
    }
}
