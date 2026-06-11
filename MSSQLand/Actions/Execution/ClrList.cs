// MSSQLand/Actions/Execution/ClrList.cs

using System;
using System.Data;

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;

namespace MSSQLand.Actions.Execution
{
    /// <summary>
    /// Enumerates all user-defined CLR assemblies registered in the current database.
    /// Shows name, CLR name, permission set, creation/modification dates, and procedure count.
    /// Use clr-inspect to drill into a specific assembly.
    /// </summary>
    internal class ClrList : BaseAction
    {
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.Task("Enumerating user-defined CLR assemblies");

            const string query = @"
SELECT
    a.name                  AS [Name],
    a.clr_name              AS [CLR Name],
    a.permission_set_desc   AS [Permission Set],
    a.create_date           AS [Created],
    a.modify_date           AS [Modified],
    COUNT(am.object_id)     AS [Procedures]
FROM sys.assemblies a
LEFT JOIN sys.assembly_modules am ON a.assembly_id = am.assembly_id
WHERE a.is_user_defined = 1
GROUP BY a.name, a.clr_name, a.permission_set_desc, a.create_date, a.modify_date
ORDER BY a.create_date DESC";

            DataTable result = databaseContext.QueryService.ExecuteTable(query);

            if (result.Rows.Count == 0)
            {
                Logger.Warning("No user-defined CLR assemblies found in the current database");
                return result;
            }

            Console.WriteLine(OutputFormatter.ConvertDataTable(result));
            Logger.Success($"Found {result.Rows.Count} user-defined assembl{(result.Rows.Count == 1 ? "y" : "ies")}");

            return result;
        }
    }
}
