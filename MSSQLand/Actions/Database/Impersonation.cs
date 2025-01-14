using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Data;


namespace MSSQLand.Actions
{
    internal class Impersonation : BaseAction
    {
        public override void ValidateArguments(string additionalArgument)
        {
            // No additional arguments needed
        }

        public override void Execute(DatabaseContext connectionManager)
        {
            Logger.TaskNested("Starting impersonation check for all logins");

            Dictionary<string, string> allLogins = new();

            // Query to obtain all SQL logins and Windows principals
            string query = "SELECT name FROM sys.server_principals WHERE type_desc IN ('SQL_LOGIN', 'WINDOWS_LOGIN') AND name NOT LIKE '##%';";
            DataTable queryResult = connectionManager.QueryService.ExecuteTable(query);

            if (queryResult.Rows.Count == 0)
            {
                Logger.Warning("No SQL logins or Windows principals found.");
                return;
            }

            // Check if the current user is a sysadmin
            if (connectionManager.UserService.IsAdmin())
            {
                Logger.Success("Current user is 'sysadmin'; it can impersonate any login.");
                foreach (DataRow row in queryResult.Rows)
                {
                    string user = row["name"].ToString();
                    allLogins.Add(user, "Yes");
                }
            }
            else
            {
                Logger.Task("Checking impersonation permissions individually");
                foreach (DataRow row in queryResult.Rows)
                {
                    string user = row["name"].ToString();
                    bool canImpersonate = connectionManager.UserService.CanImpersonate(user);
                    allLogins.Add(user, canImpersonate ? "Yes" : "No");
                }
            }


            Console.WriteLine(MarkdownFormatter.ConvertDictionaryToMarkdownTable(allLogins, "Logins", "Impersonation"));
        }
    }
}
