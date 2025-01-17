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

        public override void Execute(DatabaseContext databaseContext)
        {
            (string userName, string systemUser) = databaseContext.UserService.GetInfo();

            // Fetch roles assigned to the current user
            var rolesTable = databaseContext.QueryService.ExecuteTable(
                "SELECT sp.name AS RoleName " +
                "FROM sys.server_principals sp " +
                "INNER JOIN sys.server_role_members srm ON sp.principal_id = srm.role_principal_id " +
                "WHERE srm.member_principal_id = SUSER_ID();"
            );


            var userRoles = rolesTable.AsEnumerable()
                          .Select(row => row.Field<string>("RoleName"))
                          .ToHashSet();

            // Query for accessible databases
            DataTable accessibleDatabases = databaseContext.QueryService.ExecuteTable(
                "SELECT name FROM sys.databases WHERE HAS_DBACCESS(name) = 1;"
            );

            var databaseNames = accessibleDatabases.AsEnumerable()
                                       .Select(row => row.Field<string>("name"))
                                       .ToList();

            // Display the user information
            Logger.NewLine();
            Logger.Info("User Details:");
            Console.WriteLine(MarkdownFormatter.ConvertDictionaryToMarkdownTable(new Dictionary<string, string>
            {
                { "User Name", userName },
                { "System User", systemUser },
                { "Roles", string.Join(", ", userRoles) },
                { "Accessible Databases", string.Join(", ", databaseNames) },
            }, "Property", "Value"));


            // Define fixed server roles and their descriptions
            var fixedServerRoles = new List<(string Role, string KeyResponsibility, string Usefulness)>
            {
                ("sysadmin", "Full control over the SQL Server instance", "Target for escalation"),
                ("serveradmin", "Manage server-wide configurations", "Configure security settings, logs"),
                ("setupadmin", "Manage linked servers and setup tasks", "Linked servers can be abused"),
                ("processadmin", "Terminate and monitor processes", "Can kill admin sessions"),
                ("diskadmin", "Manage disk files for databases", "Limited impact unless combined with other roles"),
                ("dbcreator", "Create and alter databases", "Can create objects for escalation"),
                ("bulkadmin", "Perform bulk data imports", "Potential for data injection")
            };

            // Create a DataTable to display fixed server roles
            DataTable fixedServerRolesTable = new();
            fixedServerRolesTable.Columns.Add("Role", typeof(string));
            fixedServerRolesTable.Columns.Add("Key Responsibility", typeof(string));
            fixedServerRolesTable.Columns.Add("Usefulness", typeof(string));
            fixedServerRolesTable.Columns.Add("Has", typeof(bool));

            foreach (var (role, responsibility, usefulness) in fixedServerRoles)
            {
                fixedServerRolesTable.Rows.Add(role, responsibility, usefulness, userRoles.Contains(role));
            }


            // Display the fixed server roles table
            Logger.Info("Fixed Server Roles:");
            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(fixedServerRolesTable));

            

        }
    }
}
