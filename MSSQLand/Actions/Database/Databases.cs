// MSSQLand/Actions/Database/Databases.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;
using System.Linq;

namespace MSSQLand.Actions.Database
{
    /// <summary>
    /// TRUSTWORTHY property allows code within a database to access resources outside the database.
    /// When TRUSTWORTHY=ON, executing "EXECUTE AS USER = 'dbo'" inherits the server-level privileges
    /// of the database's owner login (SUSER_SNAME(owner_sid)).
    /// </summary>
    internal class Databases : BaseAction
    {
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Enumerating databases");

            DataTable allDatabases = databaseContext.QueryService.ExecuteTable(
                @"SELECT
                    d.database_id AS dbid,
                    d.name,
                    CAST(HAS_DBACCESS(d.name) AS BIT) AS Accessible,
                    d.is_trustworthy_on AS Trustworthy,
                    d.state_desc AS [State],
                    d.user_access_desc AS [Access],
                    d.is_read_only AS [ReadOnly],
                    d.recovery_model_desc AS [Recovery Model],
                    SUSER_SNAME(d.owner_sid) AS Owner,
                    d.create_date AS [Created]
                FROM sys.databases d
                ORDER BY HAS_DBACCESS(d.name) DESC, d.create_date DESC;"
            );

            Console.WriteLine(OutputFormatter.ConvertDataTable(allDatabases));

            Logger.Success($"Retrieved {allDatabases.Rows.Count} database(s).");
            return allDatabases;
        }

    }
}
