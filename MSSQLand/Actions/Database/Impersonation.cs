// MSSQLand/Actions/Database/Impersonation.cs

using System;
using System.Data;

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;

namespace MSSQLand.Actions.Database
{
    internal class Impersonation : BaseAction
    {
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.Task("Starting impersonation check");

            // Query to obtain all SQL logins and Windows principals except current user
            string query = @"SELECT
    sp.name,
    sp.type_desc,
    sp.create_date,
    sp.modify_date,
    HAS_PERMS_BY_NAME(sp.name, 'LOGIN', 'IMPERSONATE') AS can_impersonate
FROM sys.server_principals sp
WHERE sp.type_desc IN ('SQL_LOGIN', 'WINDOWS_LOGIN', 'WINDOWS_GROUP')
  AND sp.name NOT LIKE '##%'
  AND sp.name != SYSTEM_USER
ORDER BY can_impersonate DESC, sp.create_date DESC;";

            DataTable queryResult = databaseContext.QueryService.ExecuteTable(query);

            if (queryResult.Rows.Count == 0)
            {
                Logger.Warning("No SQL logins or Windows principals found.");
                return queryResult;
            }

            // Rename columns for better readability
            queryResult.Columns["name"].ColumnName = "Login";
            queryResult.Columns["type_desc"].ColumnName = "Type";
            queryResult.Columns["create_date"].ColumnName = "Created Date";
            queryResult.Columns["modify_date"].ColumnName = "Modified Date";

            // Check if the current user is a sysadmin
            if (databaseContext.UserService.IsAdmin())
            {
                Logger.Success("Current user is 'sysadmin', therefore has impersonation rights on all logins.");
                // Impersonation column is redundant when sysadmin: remove the raw bit column and show all logins as-is
                queryResult.Columns.Remove("can_impersonate");
            }
            else
            {
                // Add a readable Impersonation column before removing the raw bit column
                DataColumn impersonationColumn = new("Impersonation", typeof(string));
                queryResult.Columns.Add(impersonationColumn);
                impersonationColumn.SetOrdinal(0); // Move to first position

                foreach (DataRow row in queryResult.Rows)
                {
                    bool canImpersonate = Convert.ToInt32(row["can_impersonate"]) == 1;
                    row["Impersonation"] = canImpersonate ? "Yes" : "No";
                }

                queryResult.Columns.Remove("can_impersonate");
            }

            Console.WriteLine(OutputFormatter.ConvertDataTable(queryResult));

            Logger.Success($"Impersonation check completed for {queryResult.Rows.Count} login(s)");

            return queryResult;
        }
    }
}
