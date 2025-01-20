using MSSQLand.Services;
using MSSQLand.Utilities;
using System;

namespace MSSQLand.Actions.Administration
{
    internal class Monitor : BaseAction
    {

        public override void ValidateArguments(string additionalArguments)
        {
            // No additional arguments needed
        }

        public override void Execute(DatabaseContext databaseContext)
        {

            Logger.NewLine();
            Logger.Info("Currently running SQL commands");

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
            FROM sys.dm_exec_requests r
            CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t
            LEFT JOIN sys.dm_exec_connections c
                ON r.session_id = c.session_id
            WHERE r.session_id != @@SPID
            ORDER BY r.start_time DESC;";

            Console.WriteLine(MarkdownFormatter.ConvertSqlDataReaderToMarkdownTable(databaseContext.QueryService.Execute(currentCommandsQuery)));
        }
    }
}
