// MSSQLand/Actions/Database/Impersonation.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.Database
{
    internal class Impersonation : BaseAction
    {
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Starting impersonation check");

            // Query to obtain all SQL logins and Windows principals except current user
            string query = @"SELECT
    sp.name,
    sp.type_desc,
    sp.create_date,
    sp.modify_date,
    HAS_PERMS_BY_NAME(sp.name, 'LOGIN', 'IMPERSONATE') AS can_impersonate
FROM sys.server_principals sp
WHERE sp.type_desc IN ('SQL_LOGIN', 'WINDOWS_LOGIN')
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

            // Add a writable column for impersonation status
            DataColumn impersonationColumn = new("Impersonation", typeof(string));
            queryResult.Columns.Add(impersonationColumn);
            impersonationColumn.SetOrdinal(0); // Move to first position

            // Check if the current user is a sysadmin
            if (databaseContext.UserService.IsAdmin())
            {
                Logger.Success("Current user is 'sysadmin'; it can impersonate any login.");
                foreach (DataRow row in queryResult.Rows)
                {
                    row["Impersonation"] = "Yes";
                }
            }
            else
            {
                foreach (DataRow row in queryResult.Rows)
                {
                    // Convert bit value (1/0) to Yes/No
                    bool canImpersonate = Convert.ToInt32(row["can_impersonate"]) == 1;
                    row["Impersonation"] = canImpersonate ? "Yes" : "No";
                }
            }

            // Remove the original can_impersonate column
            queryResult.Columns.Remove("can_impersonate");

            Console.WriteLine(OutputFormatter.ConvertDataTable(queryResult));

            Logger.Success($"Impersonation check completed for {queryResult.Rows.Count} login(s)");

            return queryResult;
        }
    }
}
