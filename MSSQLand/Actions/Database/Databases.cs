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

            // Query for trustworthy databases and owner information
            DataTable databaseInfo = databaseContext.QueryService.ExecuteTable(
                @"SELECT 
                    d.name, 
                    d.is_trustworthy_on,
                    SUSER_SNAME(d.owner_sid) AS owner_name
                FROM sys.databases d;"
            );

            // Add columns for "Accessible", "Trustworthy", and "Owner"
            allDatabases.Columns.Add("Accessible", typeof(bool));
            allDatabases.Columns.Add("Trustworthy", typeof(bool));
            allDatabases.Columns.Add("Owner", typeof(string));

            // Populate "Accessible", "Trustworthy", and "Owner" columns
            foreach (DataRow row in allDatabases.Rows)
            {
                string dbName = row["name"].ToString();

                // Check accessibility
                bool isAccessible = accessibleDatabases.AsEnumerable()
                    .Any(r => r.Field<string>("name") == dbName);
                row["Accessible"] = isAccessible;

                // Get trustworthy status and owner
                var dbInfoRow = databaseInfo.AsEnumerable()
                    .FirstOrDefault(r => r.Field<string>("name") == dbName);
                
                if (dbInfoRow != null)
                {
                    row["Trustworthy"] = dbInfoRow.Field<bool>("is_trustworthy_on");
                    row["Owner"] = dbInfoRow.Field<string>("owner_name") ?? "N/A";
                }
                else
                {
                    row["Trustworthy"] = false;
                    row["Owner"] = "N/A";
                }
            }

            // Reorder columns using SetOrdinal
            allDatabases.Columns["Accessible"].SetOrdinal(2);
            allDatabases.Columns["Trustworthy"].SetOrdinal(3);
            allDatabases.Columns["Owner"].SetOrdinal(4);

            // Output the final table
            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(allDatabases));

            return allDatabases;
        }

    }
}
