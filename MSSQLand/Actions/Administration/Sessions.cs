using MSSQLand.Services;
using MSSQLand.Utilities;
using System;

namespace MSSQLand.Actions.Administration
{
    internal class Sessions : BaseAction
    {

        public override void ValidateArguments(string additionalArguments)
        {
            // No additional arguments needed
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.Info("Active SQL Server sessions");

            string sessionsQuery = @"
            SELECT 
                session_id,
                login_time,
                host_name,
                program_name,
                client_interface_name,
                login_name
            FROM sys.dm_exec_sessions
            ORDER BY login_time DESC;";

            Console.WriteLine(MarkdownFormatter.ConvertSqlDataReaderToMarkdownTable(databaseContext.QueryService.Execute(sessionsQuery)));

            return null;
        }
    }
}
