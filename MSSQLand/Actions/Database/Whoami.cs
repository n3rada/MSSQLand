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

            // Display the user information
            Logger.NewLine();
            Logger.Info("User Details:");
            
            // Only show roles where user is a member
            var userFixedRoles = fixedRoles.Where(r => r.IsMember).Select(r => r.Role);
            var userCustomRoles = customRoles.Where(r => r.IsMember).Select(r => r.Role);
            
            var userDetails = new Dictionary<string, string>
            {
                { "User Name", userName },
                { "System User", systemUser },
                { "Server Fixed Roles", string.Join(", ", userFixedRoles) },
                { "Server Custom Roles", string.Join(", ", userCustomRoles) },
                { "Database Roles", string.Join(", ", userDbRoles) },
                { "Accessible Databases", string.Join(", ", databaseNames) }
            };

            Console.WriteLine(OutputFormatter.ConvertDictionary(userDetails, "Property", "Value"));

            return null;
        }
    }
}
