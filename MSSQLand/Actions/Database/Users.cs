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
            Logger.Info("Enumerating server-level principals (logins) and their instance-wide server roles");
            Logger.InfoNested("Note: Use 'roles' action to see database-level role memberships");
            Logger.NewLine();

            string query = @"
                SELECT r.name AS Name, r.type_desc AS Type, r.is_disabled, r.create_date, r.modify_date,
                       sl.sysadmin, sl.securityadmin, sl.serveradmin, sl.setupadmin, 
                       sl.processadmin, sl.diskadmin, sl.dbcreator, sl.bulkadmin 
                FROM master.sys.server_principals r 
                LEFT JOIN master.sys.syslogins sl ON sl.sid = r.sid 
                WHERE r.type IN ('G','U','E','S','X') AND r.name NOT LIKE '##%'
                ORDER BY r.modify_date DESC;";

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
