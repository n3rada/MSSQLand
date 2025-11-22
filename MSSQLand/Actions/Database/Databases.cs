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
        public override void ValidateArguments(string[] args)
        {
            // No additional arguments needed
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            bool isAzureSQL = databaseContext.QueryService.IsAzureSQL();
            DataTable allDatabases;

            if (isAzureSQL)
            {
                // Azure SQL Database: Only show current database (can't enumerate other databases)
                // in Azure SQL Database the owner is always dbo
                allDatabases = databaseContext.QueryService.ExecuteTable(
                    @"SELECT 
                        DB_ID() AS dbid,
                        DB_NAME() AS name,
                        CAST(1 AS BIT) AS Visible,
                        CAST(1 AS BIT) AS Accessible,
                        CAST(is_trustworthy_on AS BIT) AS Trustworthy,
                        'dbo' AS Owner,
                        create_date AS crdate
                    FROM sys.databases
                    WHERE database_id = DB_ID();"
                );
            }
            else
            {
                // On-premises SQL Server: Full database enumeration
                allDatabases = databaseContext.QueryService.ExecuteTable(
                    @"SELECT 
                        db.dbid,
                        db.name,
                        CASE WHEN d.database_id IS NOT NULL THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS Visible,
                        CAST(HAS_DBACCESS(db.name) AS BIT) AS Accessible,
                        ISNULL(d.is_trustworthy_on, 0) AS Trustworthy,
                        ISNULL(SUSER_SNAME(d.owner_sid), 'N/A') AS Owner,
                        db.crdate,
                        db.filename
                    FROM master.dbo.sysdatabases db
                    LEFT JOIN master.sys.databases d ON db.name = d.name
                    ORDER BY db.name ASC, Visible DESC, Accessible DESC, Trustworthy DESC, db.crdate DESC;"
                );
            }

            // Output the final table
            Console.WriteLine(OutputFormatter.ConvertDataTable(allDatabases));

            return allDatabases;
        }

    }
}
