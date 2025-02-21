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


        public override object? Execute(DatabaseContext databaseContext)
        {
            // Query for all databases
            DataTable allDatabases = databaseContext.QueryService.ExecuteTable(
                "SELECT dbid, name, crdate, filename FROM master.dbo.sysdatabases ORDER BY crdate DESC;"
            );

            // Query for accessible databases
            DataTable accessibleDatabases = databaseContext.QueryService.ExecuteTable(
                "SELECT name FROM sys.databases WHERE HAS_DBACCESS(name) = 1;"
            );

            // Query for trustworthy databases
            DataTable trustworthyDatabases = databaseContext.QueryService.ExecuteTable(
                "SELECT name, is_trustworthy_on FROM sys.databases;"
            );

            // Add columns for "Accessible" and "Trustworthy"
            allDatabases.Columns.Add("Accessible", typeof(bool));
            allDatabases.Columns.Add("Trustworthy", typeof(bool));

            // Populate "Accessible" and "Trustworthy" columns
            foreach (DataRow row in allDatabases.Rows)
            {
                string dbName = row["name"].ToString();

                // Check accessibility
                bool isAccessible = accessibleDatabases.AsEnumerable()
                    .Any(r => r.Field<string>("name") == dbName);
                row["Accessible"] = isAccessible;

                // Check trustworthy status
                bool isTrustworthy = trustworthyDatabases.AsEnumerable()
                    .Any(r => r.Field<string>("name") == dbName && r.Field<bool>("is_trustworthy_on"));
                row["Trustworthy"] = isTrustworthy;
            }

            // Reorder columns using SetOrdinal
            allDatabases.Columns["Accessible"].SetOrdinal(2);
            allDatabases.Columns["Trustworthy"].SetOrdinal(3);

            // Output the final table
            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(allDatabases));

            return allDatabases;
        }

    }
}
