using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;
using System.Linq;

namespace MSSQLand.Actions.Database
{
    internal class Databases : BaseAction
    {
        public override void ValidateArguments(string additionalArguments)
        {
            // No additional arguments needed
        }


        public override void Execute(DatabaseContext databaseContext)
        {
            // Query for all databases
            DataTable allDatabases = databaseContext.QueryService.ExecuteTable(
                "SELECT dbid, name, crdate, filename FROM master.dbo.sysdatabases ORDER BY crdate DESC;"
            );

            // Query for accessible databases
            DataTable accessibleDatabases = databaseContext.QueryService.ExecuteTable(
                "SELECT name FROM sys.databases WHERE HAS_DBACCESS(name) = 1;"
            );

            // Add a column to indicate accessibility
            allDatabases.Columns.Add("Accessible", typeof(bool));

            // Mark each database as accessible or not
            foreach (DataRow row in allDatabases.Rows)
            {
                string dbName = row["name"].ToString();
                bool isAccessible = accessibleDatabases.AsEnumerable()
                    .Any(r => r.Field<string>("name") == dbName);
                row["Accessible"] = isAccessible;
            }

            // Reorder columns using SetOrdinal
            allDatabases.Columns["Accessible"].SetOrdinal(2); // Move "Accessible" after "name"

            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(allDatabases));
        }
    }
}
