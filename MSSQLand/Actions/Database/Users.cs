using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;
using System.Collections.Generic;
using System.Linq;


namespace MSSQLand.Actions.Database
{
    /// <summary>
    /// Enumerates server-level principals (logins) and database users.
    /// 
    /// Displays:
    /// - Server logins with their instance-wide server roles (sysadmin, securityadmin, etc.)
    /// - Database users in the current database context
    /// 
    /// For database-level role memberships, use the 'roles' action instead.
    /// </summary>
    internal class Users : BaseAction
    {
        public override void ValidateArguments(string[] args)
        {
            // No additional arguments needed
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            string databaseUsersQuery;

            if (!databaseContext.QueryService.IsAzureSQL())
            {
                // On-premises SQL Server: Show server logins and database users
                Logger.TaskNested("Enumerating server-level principals (logins) and their instance-wide server roles");
                string query = @"
                    SELECT 
                        sp.name AS Name, 
                        sp.type_desc AS Type, 
                        sp.is_disabled, 
                        sp.create_date, 
                        sp.modify_date,
                        STRING_AGG(sr.name, ', ') AS groups
                    FROM master.sys.server_principals sp
                    LEFT JOIN master.sys.server_role_members srm ON sp.principal_id = srm.member_principal_id
                    LEFT JOIN master.sys.server_principals sr ON srm.role_principal_id = sr.principal_id AND sr.type = 'R'
                    WHERE sp.type IN ('G','U','E','S','X') AND sp.name NOT LIKE '##%'
                    GROUP BY sp.name, sp.type_desc, sp.is_disabled, sp.create_date, sp.modify_date
                    ORDER BY sp.modify_date DESC;";

                DataTable rawTable = databaseContext.QueryService.ExecuteTable(query);

                Console.WriteLine(OutputFormatter.ConvertDataTable(rawTable));
            }

            Logger.TaskNested("Database users in current database context");

            databaseUsersQuery = @"
                SELECT name AS username, create_date, modify_date, type_desc AS type, 
                       authentication_type_desc AS authentication_type 
                FROM sys.database_principals 
                WHERE type NOT IN ('R', 'A', 'X') AND sid IS NOT null AND name NOT LIKE '##%' 
                ORDER BY modify_date DESC;";

            Console.WriteLine(OutputFormatter.ConvertDataTable(
                databaseContext.QueryService.ExecuteTable(databaseUsersQuery)));

            return null;
        }
    }
}
