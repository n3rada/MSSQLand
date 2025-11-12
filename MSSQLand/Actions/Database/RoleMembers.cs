using MSSQLand.Services;
using MSSQLand.Utilities;
using System;

namespace MSSQLand.Actions.Database
{
    internal class RoleMembers : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Server role name (e.g., sysadmin, serveradmin)")]
        private string _roleName;

        public override void ValidateArguments(string additionalArguments)
        {
            if (string.IsNullOrWhiteSpace(additionalArguments))
            {
                throw new ArgumentException("Role name is required. Example: sysadmin, serveradmin, securityadmin, etc.");
            }

            _roleName = additionalArguments.Trim();
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.Info($"Retrieving members of server role: {_roleName}");

            string query = $@"
SELECT 
    l.name AS LoginName,
    l.type_desc AS LoginType,
    l.is_disabled AS IsDisabled,
    l.create_date AS CreateDate,
    l.modify_date AS ModifyDate,
    l.tenant_id AS TenantId
FROM master.sys.server_role_members rm
JOIN master.sys.server_principals r ON rm.role_principal_id = r.principal_id
JOIN master.sys.server_principals l ON rm.member_principal_id = l.principal_id
WHERE r.name = '{_roleName}'
ORDER BY l.create_date DESC;";

            var result = databaseContext.QueryService.ExecuteTable(query);
            
            if (result.Rows.Count == 0)
            {
                Logger.Warning($"No members found for role '{_roleName}'. Verify the role name is correct.");
            }
            else
            {
                Logger.Success($"Found {result.Rows.Count} member(s) in role '{_roleName}'");
            }

            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(result));

            return null;
        }
    }
}
