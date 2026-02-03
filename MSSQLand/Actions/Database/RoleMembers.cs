// MSSQLand/Actions/Database/RoleMembers.cs

using System;
using System.Data;

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;

namespace MSSQLand.Actions.Database
{
    internal class RoleMembers : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Server role name (e.g., sysadmin, serveradmin)")]
        private string _roleName = string.Empty;

        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Retrieving members of server role: {_roleName}");

            string query = $@"
SELECT
    l.name AS LoginName,
    l.type_desc AS LoginType,
    l.is_disabled AS IsDisabled,
    l.create_date AS CreateDate,
    l.modify_date AS ModifyDate
FROM master.sys.server_role_members rm
JOIN master.sys.server_principals r ON rm.role_principal_id = r.principal_id
JOIN master.sys.server_principals l ON rm.member_principal_id = l.principal_id
WHERE r.name = '{_roleName}'
ORDER BY l.create_date DESC;";

            DataTable result = databaseContext.QueryService.ExecuteTable(query);

            if (result.Rows.Count == 0)
            {
                Logger.Warning($"No members found for role '{_roleName}'. Verify the role name is correct.");
                return result;
            }

            Console.WriteLine(OutputFormatter.ConvertDataTable(result));
            Logger.Success($"Found {result.Rows.Count} member(s) in role '{_roleName}'");

            return result;
        }
    }
}
