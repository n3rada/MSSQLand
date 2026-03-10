// MSSQLand/Actions/Database/Databases.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.Database
{
    /// <summary>
    /// List all available databases with accessibility, trustworthiness, owner, and file paths.
    /// </summary>
    internal class Databases : BaseAction
    {
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Enumerating databases");

            // Try the full query with physical_name first
            DataTable allDatabases = null;

            try
            {
                allDatabases = databaseContext.QueryService.ExecuteTable(
                    @"SELECT
                        d.database_id AS dbid,
                        d.name,
                        SUSER_SNAME(d.owner_sid) AS Owner,
                        d.create_date AS [Created],
                        CAST(HAS_DBACCESS(d.name) AS BIT) AS Accessible,
                        d.is_trustworthy_on AS Trustworthy,
                        d.state_desc AS [State],
                        d.user_access_desc AS [Access],
                        d.is_read_only AS [ReadOnly],
                        d.recovery_model_desc AS [Recovery Model],
                        mf.physical_name AS [MDF Path]
                    FROM sys.databases d
                    LEFT JOIN sys.master_files mf ON d.database_id = mf.database_id AND mf.file_id = 1
                    ORDER BY HAS_DBACCESS(d.name) DESC, d.name ASC;"
                );
            }
            catch (Exception ex)
            {
                // Fallback for RDS/restricted environments
                Logger.Warning("Cannot access sys.master_files");
                Logger.Debug($"Error: {ex.Message}");

                allDatabases = databaseContext.QueryService.ExecuteTable(
                    @"SELECT
                        d.database_id AS dbid,
                        d.name,
                        SUSER_SNAME(d.owner_sid) AS Owner,
                        d.create_date AS [Created],
                        CAST(HAS_DBACCESS(d.name) AS BIT) AS Accessible,
                        d.is_trustworthy_on AS Trustworthy,
                        d.state_desc AS [State],
                        d.user_access_desc AS [Access],
                        d.is_read_only AS [ReadOnly],
                        d.recovery_model_desc AS [Recovery Model]
                    FROM sys.databases d
                    ORDER BY HAS_DBACCESS(d.name) DESC, d.name ASC;"
                );
            }

            Console.WriteLine(OutputFormatter.ConvertDataTable(allDatabases));
            Logger.Success($"Retrieved {allDatabases.Rows.Count} database(s).");
            return allDatabases;
        }
    }
}
