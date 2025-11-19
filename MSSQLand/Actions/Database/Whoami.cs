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
            
            var userDetails = new Dictionary<string, string>
            {
                { "User Name", userName },
                { "System User", systemUser },
                { "Roles", string.Join(", ", userRoles) },
                { "Accessible Databases", string.Join(", ", databaseNames) }
            };

            Console.WriteLine(MarkdownFormatter.ConvertDictionaryToMarkdownTable(userDetails, "Property", "Value"));

            // Define fixed server roles descriptions
            var roleDescriptions = new Dictionary<string, string>
            {
                { "sysadmin", "Full control over the SQL Server instance" },
                { "serveradmin", "Manage server-wide configurations" },
                { "setupadmin", "Manage linked servers and setup tasks" },
                { "processadmin", "Terminate and monitor processes" },
                { "diskadmin", "Manage disk files for databases" },
                { "dbcreator", "Create and alter databases" },
                { "bulkadmin", "Perform bulk data imports" },
                { "securityadmin", "Manage logins and their properties" },
                { "public", "Default role for all users" }
            };

            // Display Fixed Server Roles
            if (fixedRoles.Any())
            {
                DataTable fixedServerRolesTable = new();
                fixedServerRolesTable.Columns.Add("Role", typeof(string));
                fixedServerRolesTable.Columns.Add("Key Responsibility", typeof(string));
                fixedServerRolesTable.Columns.Add("Has", typeof(bool));

                foreach (var (role, isMember) in fixedRoles)
                {
                    string description = roleDescriptions.TryGetValue(role, out string desc) ? desc : "Fixed server role";
                    fixedServerRolesTable.Rows.Add(role, description, isMember);
                }

                Logger.Info("Fixed Server Roles:");
                Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(fixedServerRolesTable));
            }

            // Display Custom Server Roles
            if (customRoles.Any())
            {
                Logger.NewLine();
                DataTable customServerRolesTable = new();
                customServerRolesTable.Columns.Add("Role", typeof(string));
                customServerRolesTable.Columns.Add("Has", typeof(bool));

                foreach (var (role, isMember) in customRoles)
                {
                    customServerRolesTable.Rows.Add(role, isMember);
                }

                Logger.Info("Custom Server Roles:");
                Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(customServerRolesTable));
            }

            return null;
        }
    }
}
