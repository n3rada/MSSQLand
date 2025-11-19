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
        public override void ValidateArguments(string additionalArguments)
        {
            // No additional arguments needed
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            bool isAzureSQL = databaseContext.QueryService.IsAzureSQL();

            if (isAzureSQL)
            {
                // Azure SQL Database: Only show database users (no server-level principals access)
                Logger.Info("Enumerating database users in current database context");
                Logger.InfoNested("Note: Server-level principals not accessible on Azure SQL Database");
                Logger.NewLine();

                string databaseUsersQuery = @"
                    SELECT name AS username, create_date, modify_date, type_desc AS type, 
                           authentication_type_desc AS authentication_type 
                    FROM sys.database_principals 
                    WHERE type NOT IN ('R', 'A', 'X') AND sid IS NOT null AND name NOT LIKE '##%' 
                    ORDER BY modify_date DESC;";

                Console.WriteLine(OutputFormatter.ConvertDataTable(
                    databaseContext.QueryService.ExecuteTable(databaseUsersQuery)));

                return null;
            }

            // On-premises SQL Server: Show server logins and database users
            Logger.Info("Enumerating server-level principals (logins) and their instance-wide server roles");
            Logger.InfoNested("Note: Use 'roles' action to see database-level role memberships");
            Logger.NewLine();

            string query = @"
                SELECT 
                    sp.name AS Name, 
                    sp.type_desc AS Type, 
                    sp.is_disabled, 
                    sp.create_date, 
                    sp.modify_date,
                    MAX(CASE WHEN sr.name = 'sysadmin' THEN 1 ELSE 0 END) AS sysadmin,
                    MAX(CASE WHEN sr.name = 'securityadmin' THEN 1 ELSE 0 END) AS securityadmin,
                    MAX(CASE WHEN sr.name = 'serveradmin' THEN 1 ELSE 0 END) AS serveradmin,
                    MAX(CASE WHEN sr.name = 'setupadmin' THEN 1 ELSE 0 END) AS setupadmin,
                    MAX(CASE WHEN sr.name = 'processadmin' THEN 1 ELSE 0 END) AS processadmin,
                    MAX(CASE WHEN sr.name = 'diskadmin' THEN 1 ELSE 0 END) AS diskadmin,
                    MAX(CASE WHEN sr.name = 'dbcreator' THEN 1 ELSE 0 END) AS dbcreator,
                    MAX(CASE WHEN sr.name = 'bulkadmin' THEN 1 ELSE 0 END) AS bulkadmin
                FROM master.sys.server_principals sp
                LEFT JOIN master.sys.server_role_members srm ON sp.principal_id = srm.member_principal_id
                LEFT JOIN master.sys.server_principals sr ON srm.role_principal_id = sr.principal_id AND sr.type = 'R'
                WHERE sp.type IN ('G','U','E','S','X') AND sp.name NOT LIKE '##%'
                GROUP BY sp.name, sp.type_desc, sp.is_disabled, sp.create_date, sp.modify_date
                ORDER BY sp.modify_date DESC;";

            DataTable rawTable = databaseContext.QueryService.ExecuteTable(query);

            // Post-process to create groups column from role flags
            DataTable table = new DataTable();
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("Type", typeof(string));
            table.Columns.Add("is_disabled", typeof(bool));
            table.Columns.Add("create_date", typeof(DateTime));
            table.Columns.Add("modify_date", typeof(DateTime));
            table.Columns.Add("groups", typeof(string));

            string[] roleColumns = { "sysadmin", "securityadmin", "serveradmin", "setupadmin", 
                                    "processadmin", "diskadmin", "dbcreator", "bulkadmin" };

            foreach (DataRow row in rawTable.Rows)
            {
                List<string> roles = new List<string>();
                
                foreach (string roleColumn in roleColumns)
                {
                    if (row[roleColumn] != DBNull.Value && Convert.ToBoolean(row[roleColumn]))
                    {
                        roles.Add(roleColumn);
                    }
                }

                table.Rows.Add(
                    row["Name"],
                    row["Type"],
                    row["is_disabled"],
                    row["create_date"],
                    row["modify_date"],
                    string.Join(", ", roles)
                );
            }

            Console.WriteLine(OutputFormatter.ConvertDataTable(table));
            Logger.NewLine();

            Logger.Info("Database users in current database context");

            string databaseUsersQuery = @"
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
