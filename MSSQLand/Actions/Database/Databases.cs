// MSSQLand/Actions/Database/Databases.cs

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
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Enumerating databases");

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
                        d.database_id AS dbid,
                        d.name,
                        CAST(HAS_DBACCESS(d.name) AS BIT) AS Accessible,
                        d.is_trustworthy_on AS Trustworthy,
                        SUSER_SNAME(d.owner_sid) AS Owner,
                        d.create_date AS crdate
                    FROM sys.databases d
                    WHERE d.state = 0
                    ORDER BY HAS_DBACCESS(d.name) DESC, d.create_date DESC;"
                );

                int accessibleCount = allDatabases.AsEnumerable().Count(r => Convert.ToBoolean(r["Accessible"]));
                int inaccessibleCount = allDatabases.Rows.Count - accessibleCount;

                Console.WriteLine(OutputFormatter.ConvertDataTable(allDatabases));

                Logger.Success($"Retrieved {allDatabases.Rows.Count} database(s): {accessibleCount} accessible, {inaccessibleCount} inaccessible");
                return allDatabases;
            }

            // Output for Azure SQL (single category)
            Console.WriteLine(OutputFormatter.ConvertDataTable(allDatabases));

            Logger.Success($"Retrieved {allDatabases.Rows.Count} database(s)");

            return allDatabases;
        }

    }
}
