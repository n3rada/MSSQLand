using MSSQLand.Services;
using MSSQLand.Utilities;
using System;


namespace MSSQLand.Actions.Database
{
    internal class Users : BaseAction
    {
        public override void ValidateArguments(string additionalArguments)
        {
            // No additional arguments needed
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.Info("Server principals with role memberships");

            string serverPrincipalsQuery = @"
                SELECT r.name AS Name, r.type_desc AS Type, r.is_disabled, r.create_date, r.modify_date,
                       sl.sysadmin, sl.securityadmin, sl.serveradmin, sl.setupadmin, 
                       sl.processadmin, sl.diskadmin, sl.dbcreator, sl.bulkadmin 
                FROM master.sys.server_principals r 
                LEFT JOIN master.sys.syslogins sl ON sl.sid = r.sid 
                WHERE r.type IN ('G','U','E','S','X') AND r.name NOT LIKE '##%'
                ORDER BY r.modify_date DESC;";

            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(
                databaseContext.QueryService.ExecuteTable(serverPrincipalsQuery)));

            Logger.Info("Database users");

            string databaseUsersQuery = @"
                SELECT name AS username, create_date, modify_date, type_desc AS type, 
                       authentication_type_desc AS authentication_type 
                FROM sys.database_principals 
                WHERE type NOT IN ('R', 'A', 'X') AND sid IS NOT null AND name NOT LIKE '##%' 
                ORDER BY modify_date DESC;";

            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(
                databaseContext.QueryService.ExecuteTable(databaseUsersQuery)));

            return null;
        }
    }
}
