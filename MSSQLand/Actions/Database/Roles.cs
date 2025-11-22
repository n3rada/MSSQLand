using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;
using System.Collections.Generic;
using System.Linq;

namespace MSSQLand.Actions.Database
{
    /// <summary>
    /// Enumerates database-level roles and their members in the current database.
    /// 
    /// Displays:
    /// - Fixed database roles (db_owner, db_datareader, db_datawriter, etc.) and their members
    /// - Custom database roles and their members
    /// 
    /// This provides a role-centric view showing which users belong to each database role.
    /// For server-level logins and instance-wide privileges, use the 'users' action instead.
    /// </summary>
    internal class Roles : BaseAction
    {
        public override void ValidateArguments(string[] args)
        {
            // No arguments required
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.Info("Enumerating server-level and database-level roles with their members");
            Logger.InfoNested("SERVER roles: sysadmin, serveradmin, setupadmin, etc. (instance-wide)");
            Logger.InfoNested("DATABASE roles: db_owner, db_datareader, custom roles, etc. (current database)");
            Logger.InfoNested("Members shown are direct members only (from sys.server_role_members / sys.database_role_members)");
            Logger.NewLine();

            // ========== SERVER-LEVEL ROLES ==========
            Logger.Info("Server-Level Roles");
            Logger.NewLine();

            string serverRolesQuery = @"
                SELECT 
                    r.name AS RoleName,
                    r.is_fixed_role AS IsFixedRole,
                    r.type_desc AS RoleType,
                    r.create_date AS CreateDate,
                    r.modify_date AS ModifyDate
                FROM sys.server_principals r
                WHERE r.type = 'R'
                ORDER BY r.is_fixed_role DESC, r.name;";

            var allServerRoles = databaseContext.QueryService.ExecuteTable(serverRolesQuery);

            if (allServerRoles.Rows.Count > 0)
            {
                // Get all server role members in a single query
                string serverMembersQuery = @"
                    SELECT 
                        r.name AS role_name,
                        m.name AS member_name
                    FROM sys.server_principals r
                    INNER JOIN sys.server_role_members srm ON r.principal_id = srm.role_principal_id
                    INNER JOIN sys.server_principals m ON srm.member_principal_id = m.principal_id
                    WHERE r.type = 'R'
                    ORDER BY r.name, m.name;";

                var serverMembers = databaseContext.QueryService.ExecuteTable(serverMembersQuery);

                // Build dictionary for server role members
                var serverMembersDict = new Dictionary<string, List<string>>();
                foreach (DataRow memberRow in serverMembers.Rows)
                {
                    string roleName = memberRow["role_name"].ToString();
                    string memberName = memberRow["member_name"].ToString();

                    if (!serverMembersDict.ContainsKey(roleName))
                    {
                        serverMembersDict[roleName] = new List<string>();
                    }
                    serverMembersDict[roleName].Add(memberName);
                }

                // Add Members column
                allServerRoles.Columns.Add("Members", typeof(string));

                // Map members to server roles
                foreach (DataRow roleRow in allServerRoles.Rows)
                {
                    string roleName = roleRow["RoleName"].ToString();

                    if (serverMembersDict.TryGetValue(roleName, out var members))
                    {
                        roleRow["Members"] = string.Join(", ", members);
                    }
                    else
                    {
                        roleRow["Members"] = "";
                    }
                }

                // Separate fixed and custom server roles
                var fixedServerRoles = allServerRoles.AsEnumerable()
                    .Where(row => Convert.ToBoolean(row["IsFixedRole"]))
                    .ToList();

                var customServerRoles = allServerRoles.AsEnumerable()
                    .Where(row => !Convert.ToBoolean(row["IsFixedRole"]))
                    .ToList();

                // Display Fixed Server Roles
                if (fixedServerRoles.Any())
                {
                    DataTable fixedServerRolesTable = new DataTable();
                    fixedServerRolesTable.Columns.Add("RoleName", typeof(string));
                    fixedServerRolesTable.Columns.Add("RoleType", typeof(string));
                    fixedServerRolesTable.Columns.Add("CreateDate", typeof(DateTime));
                    fixedServerRolesTable.Columns.Add("ModifyDate", typeof(DateTime));
                    fixedServerRolesTable.Columns.Add("Members", typeof(string));

                    foreach (var row in fixedServerRoles)
                    {
                        fixedServerRolesTable.Rows.Add(
                            row["RoleName"],
                            row["RoleType"],
                            row["CreateDate"],
                            row["ModifyDate"],
                            row["Members"]
                        );
                    }

                    Logger.Success($"Fixed Server Roles ({fixedServerRoles.Count} roles)");
                    Console.WriteLine(OutputFormatter.ConvertDataTable(fixedServerRolesTable));
                    Console.WriteLine();
                }

                // Display Custom Server Roles
                if (customServerRoles.Any())
                {
                    DataTable customServerRolesTable = new DataTable();
                    customServerRolesTable.Columns.Add("RoleName", typeof(string));
                    customServerRolesTable.Columns.Add("RoleType", typeof(string));
                    customServerRolesTable.Columns.Add("CreateDate", typeof(DateTime));
                    customServerRolesTable.Columns.Add("ModifyDate", typeof(DateTime));
                    customServerRolesTable.Columns.Add("Members", typeof(string));

                    foreach (var row in customServerRoles)
                    {
                        customServerRolesTable.Rows.Add(
                            row["RoleName"],
                            row["RoleType"],
                            row["CreateDate"],
                            row["ModifyDate"],
                            row["Members"]
                        );
                    }

                    Logger.Success($"Custom Server Roles ({customServerRoles.Count} roles)");
                    Console.WriteLine(OutputFormatter.ConvertDataTable(customServerRolesTable));
                    Console.WriteLine();
                }
            }

            // ========== DATABASE-LEVEL ROLES ==========
            Logger.Info($"Database-Level Roles ({databaseContext.QueryService.ExecutionDatabase})");
            Logger.NewLine();

            // Query all database roles (both fixed and custom)
            // Fixed roles: db_owner, db_datareader, db_datawriter, db_securityadmin, etc.
            // Custom roles: user-defined roles created with CREATE ROLE
            string query = @"
SELECT 
    r.name AS RoleName,
    r.is_fixed_role AS IsFixedRole,
    r.type_desc AS RoleType,
    r.create_date AS CreateDate,
    r.modify_date AS ModifyDate
FROM sys.database_principals r
WHERE r.type = 'R'
ORDER BY r.is_fixed_role DESC, r.name;";

            var allRoles = databaseContext.QueryService.ExecuteTable(query);
            
            if (allRoles.Rows.Count == 0)
            {
                Logger.Warning("No database roles found in current database.");
                return null;
            }

            // Get all role members in a single query for performance
            // This retrieves direct role memberships from sys.database_role_members
            // Note: Does not include indirect memberships (e.g., via AD groups)
            string allMembersQuery = @"
                SELECT 
                    r.name AS role_name,
                    m.name AS member_name
                FROM sys.database_principals r
                INNER JOIN sys.database_role_members rm ON r.principal_id = rm.role_principal_id
                INNER JOIN sys.database_principals m ON rm.member_principal_id = m.principal_id
                WHERE r.type = 'R'
                ORDER BY r.name, m.name;";

            var allMembers = databaseContext.QueryService.ExecuteTable(allMembersQuery);

            // Build a dictionary for O(1) lookup: key = role_name, value = list of member names
            var membersDict = new Dictionary<string, List<string>>();
            
            foreach (DataRow memberRow in allMembers.Rows)
            {
                string roleName = memberRow["role_name"].ToString();
                string memberName = memberRow["member_name"].ToString();

                if (!membersDict.ContainsKey(roleName))
                {
                    membersDict[roleName] = new List<string>();
                }
                membersDict[roleName].Add(memberName);
            }

            // Add Members column
            allRoles.Columns.Add("Members", typeof(string));

            // Map members to roles
            foreach (DataRow roleRow in allRoles.Rows)
            {
                string roleName = roleRow["RoleName"].ToString();

                if (membersDict.TryGetValue(roleName, out var members))
                {
                    roleRow["Members"] = string.Join(", ", members);
                }
                else
                {
                    roleRow["Members"] = "";
                }
            }

            // Separate fixed roles from custom roles
            var fixedRolesData = allRoles.AsEnumerable()
                .Where(row => Convert.ToBoolean(row["IsFixedRole"]))
                .ToList();

            var customRolesData = allRoles.AsEnumerable()
                .Where(row => !Convert.ToBoolean(row["IsFixedRole"]))
                .ToList();

            // Display Fixed Roles
            if (fixedRolesData.Any())
            {
                DataTable fixedRolesTable = new DataTable();
                fixedRolesTable.Columns.Add("RoleName", typeof(string));
                fixedRolesTable.Columns.Add("RoleType", typeof(string));
                fixedRolesTable.Columns.Add("CreateDate", typeof(DateTime));
                fixedRolesTable.Columns.Add("ModifyDate", typeof(DateTime));
                fixedRolesTable.Columns.Add("Members", typeof(string));
                
                foreach (var row in fixedRolesData)
                {
                    fixedRolesTable.Rows.Add(
                        row["RoleName"],
                        row["RoleType"],
                        row["CreateDate"],
                        row["ModifyDate"],
                        row["Members"]
                    );
                }

                Logger.Success($"Fixed Database Roles ({fixedRolesData.Count} roles)");
                Console.WriteLine(OutputFormatter.ConvertDataTable(fixedRolesTable));
                Console.WriteLine();
            }

            // Display Custom Roles
            if (customRolesData.Any())
            {
                DataTable customRolesTable = new DataTable();
                customRolesTable.Columns.Add("RoleName", typeof(string));
                customRolesTable.Columns.Add("RoleType", typeof(string));
                customRolesTable.Columns.Add("CreateDate", typeof(DateTime));
                customRolesTable.Columns.Add("ModifyDate", typeof(DateTime));
                customRolesTable.Columns.Add("Members", typeof(string));
                
                foreach (var row in customRolesData)
                {
                    customRolesTable.Rows.Add(
                        row["RoleName"],
                        row["RoleType"],
                        row["CreateDate"],
                        row["ModifyDate"],
                        row["Members"]
                    );
                }

                Logger.Success($"Custom Database Roles ({customRolesData.Count} roles)");
                Console.WriteLine(OutputFormatter.ConvertDataTable(customRolesTable));
            }
            else
            {
                Logger.Info("No custom database roles found in current database.");
            }

            return null;
        }
    }
}
