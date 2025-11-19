using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
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
            // Query for all databases with accessibility, trustworthy status, and owner in a single query
            DataTable allDatabases = databaseContext.QueryService.ExecuteTable(
                @"SELECT 
                    db.dbid,
                    db.name,
                    CAST(HAS_DBACCESS(db.name) AS BIT) AS Accessible,
                    d.is_trustworthy_on AS Trustworthy,
                    SUSER_SNAME(d.owner_sid) AS Owner,
                    db.crdate,
                    db.filename
                FROM master.dbo.sysdatabases db
                LEFT JOIN master.sys.databases d ON db.name = d.name
                ORDER BY db.name ASC, Accessible DESC, Trustworthy DESC, db.crdate DESC;"
            );

            // Output the final table
            Console.WriteLine(OutputFormatter.ConvertDataTable(allDatabases));

            return allDatabases;
        }

    }
}
