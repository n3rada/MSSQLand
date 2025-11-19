using MSSQLand.Services;
using MSSQLand.Utilities;
using System.Collections.Generic;
using System;
using System.Data;
using System.Linq;

namespace MSSQLand.Actions.Database
{
    internal class Whoami : BaseAction
    {
        public override void ValidateArguments(string additionalArguments)
        {
            // No additional arguments needed
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            (string userName, string systemUser) = databaseContext.UserService.GetInfo();

            // Get all roles and check membership in a single query
            // This uses IS_SRVROLEMEMBER which works even with AD group-based access
            string rolesQuery = @"
                SELECT 
                    name,
                    is_fixed_role,
                    ISNULL(IS_SRVROLEMEMBER(name), 0) AS is_member
                FROM sys.server_principals 
                WHERE type = 'R' 
                ORDER BY is_fixed_role DESC, name;";

            DataTable allRolesTable = databaseContext.QueryService.ExecuteTable(rolesQuery);

            var fixedRoles = new List<(string Role, bool IsMember)>();
            var customRoles = new List<(string Role, bool IsMember)>();
            var userRoles = new HashSet<string>();

            // Separate roles and collect user memberships
            foreach (DataRow roleRow in allRolesTable.Rows)
            {
                string roleName = roleRow["name"].ToString();
                bool isFixedRole = Convert.ToBoolean(roleRow["is_fixed_role"]);
                bool isMember = Convert.ToInt32(roleRow["is_member"]) == 1;

                if (isFixedRole)
                {
                    fixedRoles.Add((roleName, isMember));
                }
                else
                {
                    customRoles.Add((roleName, isMember));
                }

                if (isMember)
                {
                    userRoles.Add(roleName);
                }
            }

            // Query for accessible databases
            DataTable accessibleDatabases = databaseContext.QueryService.ExecuteTable(
                "SELECT name FROM master.sys.databases WHERE HAS_DBACCESS(name) = 1;"
            );

            var databaseNames = accessibleDatabases.AsEnumerable()
                                       .Select(row => row.Field<string>("name"))
                                       .ToList();

            // Display the user information
            Logger.NewLine();
            Logger.Info("User Details:");
            
            var fixedRolesWithMembership = fixedRoles.Select(r => r.IsMember ? $"**{r.Role}**" : r.Role);
            var customRolesWithMembership = customRoles.Select(r => r.IsMember ? $"**{r.Role}**" : r.Role);
            
            var userDetails = new Dictionary<string, string>
            {
                { "User Name", userName },
                { "System User", systemUser },
                { "Fixed Roles", string.Join(", ", fixedRolesWithMembership) },
                { "Custom Roles", string.Join(", ", customRolesWithMembership) },
                { "Accessible Databases", string.Join(", ", databaseNames) }
            };

            Console.WriteLine(MarkdownFormatter.ConvertDictionaryToMarkdownTable(userDetails, "Property", "Value"));

            return null;
        }
    }
}
