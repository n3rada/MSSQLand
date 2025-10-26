﻿using MSSQLand.Services;
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
            string query = @"SELECT name, type_desc, create_date, modify_date
FROM sys.server_principals
WHERE type_desc IN ('SQL_LOGIN', 'WINDOWS_LOGIN') AND name NOT LIKE '##%'
ORDER BY create_date DESC;";
            
            DataTable queryResult = databaseContext.QueryService.ExecuteTable(query);

            if (queryResult.Rows.Count == 0)
            {
                Logger.Warning("No SQL logins or Windows principals found.");
                return queryResult;
            }

            // Add a writable column for impersonation status at the first position
            DataColumn impersonationColumn = new("Impersonation", typeof(string))
            {
                DefaultValue = "No"
            };

            queryResult.Columns.Add(impersonationColumn);
            impersonationColumn.SetOrdinal(0); // Move to first position

            // Rename columns for better readability
            queryResult.Columns["name"].ColumnName = "Login";
            queryResult.Columns["type_desc"].ColumnName = "Type";
            queryResult.Columns["create_date"].ColumnName = "Created Date";
            queryResult.Columns["modify_date"].ColumnName = "Modified Date";

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
                    string user = row["Login"].ToString();
                    bool canImpersonate = databaseContext.UserService.CanImpersonate(user);
                    row["Impersonation"] = canImpersonate ? "Yes" : "No";
                }
            }

            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(queryResult));

            return queryResult;
        }
    }
}
