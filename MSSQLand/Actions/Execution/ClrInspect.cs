// MSSQLand/Actions/Execution/ClrInspect.cs

using System;
using System.Data;

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;

namespace MSSQLand.Actions.Execution
{
    /// <summary>
    /// Inspects a specific user-defined CLR assembly in the current database.
    /// Displays assembly metadata (CLR name, permission set, dates) and the full list
    /// of stored procedures or functions it exports, including the mapped .NET class
    /// and method name for each.
    /// </summary>
    internal class ClrInspect : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Assembly name to inspect")]
        private string _assemblyName = string.Empty;

        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.Task($"Inspecting assembly '{_assemblyName}'");

            string safeName = _assemblyName.Replace("'", "''");

            string checkQuery = $@"
SELECT name, clr_name, permission_set_desc, create_date, modify_date
FROM sys.assemblies
WHERE is_user_defined = 1 AND name = '{safeName}'";

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

            string modulesQuery = $@"
SELECT
    o.name              AS [Object],
    o.type_desc         AS [Type],
    am.assembly_class   AS [Class],
    am.assembly_method  AS [Method]
FROM sys.assembly_modules am
JOIN sys.objects o ON am.object_id = o.object_id
JOIN sys.assemblies a ON am.assembly_id = a.assembly_id
WHERE a.name = '{safeName}'";

            DataTable modules = databaseContext.QueryService.ExecuteTable(modulesQuery);

            if (modules.Rows.Count == 0)
            {
                Logger.Warning("No stored procedures or functions registered for this assembly");
                return meta;
            }

            Logger.Task("Registered procedures / functions");
            Console.WriteLine(OutputFormatter.ConvertDataTable(modules));

            return modules;
        }
    }
}
