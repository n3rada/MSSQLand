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

            // Fetch roles assigned to the current user
            var rolesTable = databaseContext.QueryService.ExecuteTable(
                "SELECT sp.name AS RoleName " +
                "FROM master.sys.server_principals sp " +
                "INNER JOIN master.sys.server_role_members srm ON sp.principal_id = srm.role_principal_id " +
                "WHERE srm.member_principal_id = SUSER_ID();"
            );


            var userRoles = rolesTable.AsEnumerable()
                          .Select(row => row.Field<string>("RoleName"))
                          .ToHashSet();

            // Query for accessible databases
            DataTable accessibleDatabases = databaseContext.QueryService.ExecuteTable(
                "SELECT name FROM master.sys.databases WHERE HAS_DBACCESS(name) = 1;"
            );

            var databaseNames = accessibleDatabases.AsEnumerable()
                                       .Select(row => row.Field<string>("name"))
                                       .ToList();

            // Try to get AD group memberships
            List<string> adGroups = GetActiveDirectoryGroups(databaseContext, systemUser);

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

            if (adGroups.Count > 0)
            {
                userDetails.Add("AD Group Memberships", string.Join(", ", adGroups));
            }

            Console.WriteLine(MarkdownFormatter.ConvertDictionaryToMarkdownTable(userDetails, "Property", "Value"));


            // Define fixed server roles and their descriptions
            var fixedServerRoles = new List<(string Role, string KeyResponsibility)>
            {
                ("sysadmin", "Full control over the SQL Server instance"),
                ("serveradmin", "Manage server-wide configurations"),
                ("setupadmin", "Manage linked servers and setup tasks"),
                ("processadmin", "Terminate and monitor processes"),
                ("diskadmin", "Manage disk files for databases"),
                ("dbcreator", "Create and alter databases"),
                ("bulkadmin", "Perform bulk data imports")
            };

            // Create a DataTable to display fixed server roles
            DataTable fixedServerRolesTable = new();
            fixedServerRolesTable.Columns.Add("Role", typeof(string));
            fixedServerRolesTable.Columns.Add("Key Responsibility", typeof(string));
            fixedServerRolesTable.Columns.Add("Has", typeof(bool));

            foreach (var (role, responsibility) in fixedServerRoles)
            {
                fixedServerRolesTable.Rows.Add(role, responsibility, userRoles.Contains(role));
            }


            // Display the fixed server roles table
            Logger.Info("Fixed Server Roles:");
            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(fixedServerRolesTable));

            return null;

        }

        /// <summary>
        /// Attempts to retrieve Active Directory group memberships for the current user.
        /// </summary>
        private List<string> GetActiveDirectoryGroups(DatabaseContext databaseContext, string systemUser)
        {
            List<string> groups = new();

            try
            {
                // Check if xp_logininfo is available
                var xprocCheck = databaseContext.QueryService.ExecuteTable(
                    "SELECT * FROM master.sys.all_objects WHERE name = 'xp_logininfo' AND type = 'X';"
                );

                if (xprocCheck.Rows.Count == 0)
                {
                    Logger.Debug("xp_logininfo not available for AD group enumeration");
                    return groups;
                }

                // Try to get group memberships using xp_logininfo
                string query = $"EXEC xp_logininfo @acctname = '{systemUser}', @option = 'all';";
                DataTable groupsTable = databaseContext.QueryService.ExecuteTable(query);

                if (groupsTable != null && groupsTable.Rows.Count > 0)
                {
                    foreach (DataRow row in groupsTable.Rows)
                    {
                        // xp_logininfo returns columns: account name, type, privilege, mapped login name, permission path
                        string accountName = row["account name"]?.ToString();
                        string type = row["type"]?.ToString();
                        string permissionPath = row["permission path"]?.ToString();

                        // Add groups (not the user itself)
                        if (!string.IsNullOrEmpty(permissionPath) && 
                            !permissionPath.Equals(systemUser, StringComparison.OrdinalIgnoreCase) &&
                            type?.IndexOf("group", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            groups.Add(permissionPath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Could not retrieve AD group memberships: {ex.Message}");
            }

            return groups;
        }
    }
}
