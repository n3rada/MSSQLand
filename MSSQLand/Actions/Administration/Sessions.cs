// MSSQLand/Actions/Administration/Sessions.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;

namespace MSSQLand.Actions.Administration
{
    internal class Sessions : BaseAction
    {
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Retrieving active SQL Server sessions");

            string sessionsQuery = @"
            SELECT 
                session_id,
                login_time,
                host_name,
                program_name,
                client_interface_name,
                login_name
            FROM master.sys.dm_exec_sessions
            ORDER BY login_time DESC;";

            var result = databaseContext.QueryService.Execute(sessionsQuery);
            Console.WriteLine(OutputFormatter.ConvertSqlDataReader(result));
        
            return null;
        }
    }
}
