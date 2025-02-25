using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;

namespace MSSQLand.Actions.Database
{
    internal class Impersonation : BaseAction
    {
        public override void ValidateArguments(string additionalArguments)
        {
            // No additional arguments needed
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Starting impersonation check");

            // Query to obtain all SQL logins and Windows principals
            string query = "SELECT name FROM sys.server_principals WHERE type_desc IN ('SQL_LOGIN', 'WINDOWS_LOGIN') AND name NOT LIKE '##%';";
            DataTable queryResult = databaseContext.QueryService.ExecuteTable(query);

            if (queryResult.Rows.Count == 0)
            {
                Logger.Warning("No SQL logins or Windows principals found.");
                return queryResult;
            }

            // Add a writable column for impersonation status
            DataColumn impersonationColumn = new("Impersonation", typeof(string))
            {
                DefaultValue = "No"
            };

            queryResult.Columns.Add(impersonationColumn);

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
                Logger.TaskNested("Checking impersonation permissions individually");
                foreach (DataRow row in queryResult.Rows)
                {
                    string user = row["name"].ToString();
                    bool canImpersonate = databaseContext.UserService.CanImpersonate(user);
                    row["Impersonation"] = canImpersonate ? "Yes" : "No";
                }
            }

            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(queryResult));

            return queryResult;
        }
    }
}
