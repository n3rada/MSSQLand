using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;
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
        public override void ValidateArguments(string additionalArguments)
        {
            // No arguments required
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.Info("Enumerating database-level roles and their members in current database");
            Logger.InfoNested("Note: Use 'users' action to see server-level logins and instance-wide privileges");
            Logger.NewLine();

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

            // Add Members column
            allRoles.Columns.Add("Members", typeof(string));

            // Get members for each role
            foreach (DataRow roleRow in allRoles.Rows)
            {
                string roleName = roleRow["RoleName"].ToString();
                
                string membersQuery = $@"
                    SELECT m.name
                    FROM sys.database_principals r
                    INNER JOIN sys.database_role_members rm ON r.principal_id = rm.role_principal_id
                    INNER JOIN sys.database_principals m ON rm.member_principal_id = m.principal_id
                    WHERE r.name = '{roleName.Replace("'", "''")}'
                    ORDER BY m.name;";

                var membersTable = databaseContext.QueryService.ExecuteTable(membersQuery);
                
                var membersList = new List<string>();
                foreach (DataRow memberRow in membersTable.Rows)
                {
                    membersList.Add(memberRow["name"].ToString());
                }

                roleRow["Members"] = membersList.Count > 0 ? string.Join(", ", membersList) : "No members";
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
                Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(fixedRolesTable));
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
                Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(customRolesTable));
            }
            else
            {
                Logger.Info("No custom database roles found in current database.");
            }

            return null;
        }
    }
}
