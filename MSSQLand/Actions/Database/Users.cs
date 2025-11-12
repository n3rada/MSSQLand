using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;
using System.Collections.Generic;
using System.Linq;


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

            // Try comprehensive query first (requires VIEW ANY DEFINITION or VIEW SERVER STATE)
            string comprehensiveQuery = @"
                SELECT r.name AS Name, r.type_desc AS Type, r.is_disabled, r.create_date, r.modify_date,
                       STUFF((
                           SELECT ', ' + role.name
                           FROM master.sys.server_role_members srm
                           INNER JOIN master.sys.server_principals role ON srm.role_principal_id = role.principal_id
                           WHERE srm.member_principal_id = r.principal_id
                           FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, '') AS groups
                FROM master.sys.server_principals r 
                WHERE r.type IN ('G','U','E','S','X') AND r.name NOT LIKE '##%'
                ORDER BY r.modify_date DESC;";

            DataTable table;
            
            try
            {
                table = databaseContext.QueryService.ExecuteTable(comprehensiveQuery);
            }
            catch
            {
                // Fallback to basic fixed server roles query (works with lower privileges)
                Logger.Warning("Insufficient permissions for comprehensive role query, using basic fixed roles");
                
                string basicQuery = @"
                    SELECT r.name AS Name, r.type_desc AS Type, r.is_disabled, r.create_date, r.modify_date,
                           sl.sysadmin, sl.securityadmin, sl.serveradmin, sl.setupadmin, 
                           sl.processadmin, sl.diskadmin, sl.dbcreator, sl.bulkadmin 
                    FROM master.sys.server_principals r 
                    LEFT JOIN master.sys.syslogins sl ON sl.sid = r.sid 
                    WHERE r.type IN ('G','U','E','S','X') AND r.name NOT LIKE '##%'
                    ORDER BY r.modify_date DESC;";

                DataTable rawTable = databaseContext.QueryService.ExecuteTable(basicQuery);
                
                // Transform to groups format
                table = new DataTable();
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
                    List<string> groups = new List<string>();
                    
                    foreach (string roleColumn in roleColumns)
                    {
                        if (row[roleColumn] != DBNull.Value && Convert.ToBoolean(row[roleColumn]))
                        {
                            groups.Add(roleColumn);
                        }
                    }

                    table.Rows.Add(
                        row["Name"],
                        row["Type"],
                        row["is_disabled"],
                        row["create_date"],
                        row["modify_date"],
                        string.Join(", ", groups)
                    );
                }
            }

            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(table));

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
