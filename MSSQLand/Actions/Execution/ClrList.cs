// MSSQLand/Actions/Execution/ClrList.cs

using System;
using System.Data;

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;

namespace MSSQLand.Actions.Execution
{
    /// <summary>
    /// Lists user-defined CLR assemblies registered in the current database.
    /// With an assembly name, shows the stored procedures and functions it exports.
    /// </summary>
    internal class ClrList : BaseAction
    {
        [ArgumentMetadata(Position = 0, Description = "Assembly name to inspect (omit to list all user-defined assemblies)")]
        private string _assemblyName = null;

        public override object Execute(DatabaseContext databaseContext)
        {
            if (string.IsNullOrEmpty(_assemblyName))
            {
                return ListAssemblies(databaseContext);
            }

            return ShowModules(databaseContext);
        }

        private object ListAssemblies(DatabaseContext databaseContext)
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
ORDER BY a.create_date DESC;";

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

        private object ShowModules(DatabaseContext databaseContext)
        {
            Logger.Task($"Inspecting assembly '{_assemblyName}'");

            // Verify the assembly exists and is user-defined
            string checkQuery = $@"
SELECT name, clr_name, permission_set_desc, create_date, modify_date
FROM sys.assemblies
WHERE is_user_defined = 1 AND name = '{_assemblyName.Replace("'", "''")}';";

            DataTable meta = databaseContext.QueryService.ExecuteTable(checkQuery);

            if (meta.Rows.Count == 0)
            {
                Logger.Error($"Assembly '{_assemblyName}' not found or is not user-defined");
                return null;
            }

            DataRow row = meta.Rows[0];
            Logger.Info($"CLR name    : {row["clr_name"]}");
            Logger.Info($"Permission  : {row["permission_set_desc"]}");
            Logger.Info($"Created     : {row["create_date"]}");
            Logger.Info($"Modified    : {row["modify_date"]}");
            Logger.NewLine();

            // Show exported procedures / functions
            string modulesQuery = $@"
SELECT
    o.name              AS [Object],
    o.type_desc         AS [Type],
    am.assembly_class   AS [Class],
    am.assembly_method  AS [Method]
FROM sys.assembly_modules am
JOIN sys.objects o ON am.object_id = o.object_id
JOIN sys.assemblies a ON am.assembly_id = a.assembly_id
WHERE a.name = '{_assemblyName.Replace("'", "''")}';";

            DataTable modules = databaseContext.QueryService.ExecuteTable(modulesQuery);

            if (modules.Rows.Count == 0)
            {
                Logger.Warning("No stored procedures or functions registered for this assembly");
                return meta;
            }

            Logger.TaskNested("Registered procedures / functions");
            Console.WriteLine(OutputFormatter.ConvertDataTable(modules));

            return modules;
        }
    }
}
