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
    r.modify_date AS ModifyDate,
    ISNULL(m.name, '') AS MemberName,
    ISNULL(m.type_desc, '') AS MemberType
FROM sys.database_principals r
LEFT JOIN sys.database_role_members rm ON r.principal_id = rm.role_principal_id
LEFT JOIN sys.database_principals m ON rm.member_principal_id = m.principal_id
WHERE r.type = 'R'
ORDER BY r.is_fixed_role DESC, r.name, m.name;";

            var allRoles = databaseContext.QueryService.ExecuteTable(query);
            
            if (allRoles.Rows.Count == 0)
            {
                Logger.Warning("No database roles found in current database.");
                return null;
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
                DataTable fixedRolesTable = allRoles.Clone();
                fixedRolesTable.Columns.Remove("IsFixedRole"); // Remove the flag column for display
                
                foreach (var row in fixedRolesData)
                {
                    var newRow = fixedRolesTable.NewRow();
                    newRow["RoleName"] = row["RoleName"];
                    newRow["RoleType"] = row["RoleType"];
                    newRow["CreateDate"] = row["CreateDate"];
                    newRow["ModifyDate"] = row["ModifyDate"];
                    newRow["MemberName"] = string.IsNullOrEmpty(row["MemberName"].ToString()) ? "No members" : row["MemberName"];
                    newRow["MemberType"] = row["MemberType"];
                    fixedRolesTable.Rows.Add(newRow);
                }

                Logger.Success($"Fixed Database Roles ({fixedRolesData.Count} entries)");
                Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(fixedRolesTable));
                Console.WriteLine();
            }

            // Display Custom Roles
            if (customRolesData.Any())
            {
                DataTable customRolesTable = allRoles.Clone();
                customRolesTable.Columns.Remove("IsFixedRole"); // Remove the flag column for display
                
                foreach (var row in customRolesData)
                {
                    var newRow = customRolesTable.NewRow();
                    newRow["RoleName"] = row["RoleName"];
                    newRow["RoleType"] = row["RoleType"];
                    newRow["CreateDate"] = row["CreateDate"];
                    newRow["ModifyDate"] = row["ModifyDate"];
                    newRow["MemberName"] = string.IsNullOrEmpty(row["MemberName"].ToString()) ? "No members" : row["MemberName"];
                    newRow["MemberType"] = row["MemberType"];
                    customRolesTable.Rows.Add(newRow);
                }

                Logger.Success($"Custom Database Roles ({customRolesData.Count} entries)");
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
