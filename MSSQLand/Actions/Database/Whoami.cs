// MSSQLand/Actions/Database/Whoami.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System.Collections.Generic;
using System;
using System.Data;
using System.Linq;

namespace MSSQLand.Actions.Database
{
    internal class Whoami : BaseAction
    {
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Retrieving current user information");

            (string userName, string systemUser) = databaseContext.UserService.GetInfo();

            var (fixedServerRoles, customServerRoles) = databaseContext.UserService.GetServerRoles();

            // Query for accessible databases
            DataTable accessibleDatabases = databaseContext.QueryService.ExecuteTable(
                "SELECT name FROM master.sys.databases WHERE HAS_DBACCESS(name) = 1;"
            );

            var databaseNames = accessibleDatabases.AsEnumerable()
                                       .Select(row => row.Field<string>("name"))
                                       .ToList();

            // Get database roles in current database
            string dbRolesQuery = @"
                SELECT
                    name,
                    ISNULL(IS_ROLEMEMBER(name), 0) AS is_member
                FROM sys.database_principals
                WHERE type = 'R'
                ORDER BY name;";

            DataTable dbRolesTable = databaseContext.QueryService.ExecuteTable(dbRolesQuery);

            var userDbRoles = new List<string>();
            foreach (DataRow dbRoleRow in dbRolesTable.Rows)
            {
                if (Convert.ToInt32(dbRoleRow["is_member"]) == 1)
                {
                    userDbRoles.Add(dbRoleRow["name"].ToString());
                }
            }


            // Only show roles where user is a member
            var userDetails = new Dictionary<string, string>
            {
                { "Login", systemUser },
                { "Mapped to user", userName },
                { "Server Fixed Roles", string.Join(", ", fixedServerRoles) },
                { "Server Custom Roles", string.Join(", ", customServerRoles) },
                { "Database Roles", string.Join(", ", userDbRoles) },
                { "Accessible Databases", string.Join(", ", databaseNames) }
            };

            Console.WriteLine(OutputFormatter.ConvertDictionary(userDetails, "Property", "Value"));

            return userDetails;
        }
    }
}
