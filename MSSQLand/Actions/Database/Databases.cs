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
    /// List all available databases with accessibility, trustworthiness, owner, and file paths.
    /// </summary>
    internal class Databases : BaseAction
    {
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Enumerating databases");

            // Try the full query with physical_name first
            DataTable allDatabases;
            try
            {
                allDatabases = databaseContext.QueryService.ExecuteTable(
                    @"SELECT
                        d.database_id AS dbid,
                        d.create_date AS [Created],
                        d.name,
                        SUSER_SNAME(d.owner_sid) AS Owner,
                        CAST(IS_SRVROLEMEMBER('sysadmin', SUSER_SNAME(d.owner_sid)) AS BIT) AS OwnerIsSysadmin,
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
                        d.create_date AS [Created],
                        d.name,
                        SUSER_SNAME(d.owner_sid) AS Owner,
                        CAST(IS_SRVROLEMEMBER('sysadmin', SUSER_SNAME(d.owner_sid)) AS BIT) AS OwnerIsSysadmin,
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

            // Check db_owner role membership for each accessible database
            // IS_MEMBER is context-dependent and must be evaluated per-database
            allDatabases.Columns.Add("db_owner", typeof(bool));
            allDatabases.Columns["db_owner"].SetOrdinal(allDatabases.Columns["OwnerIsSysadmin"].Ordinal + 1);

            foreach (DataRow row in allDatabases.Rows)
            {
                bool isAccessible = row["Accessible"] != DBNull.Value && Convert.ToBoolean(row["Accessible"]);
                if (isAccessible)
                {
                    try
                    {
                        string dbName = row["name"].ToString().Replace("]", "]]");
                        object result = databaseContext.QueryService.ExecuteScalar(
                            $"EXECUTE('USE [{dbName}]; SELECT CAST(IS_MEMBER(''db_owner'') AS BIT);')"
                        );

                        row["db_owner"] = result != null && result != DBNull.Value && Convert.ToBoolean(result);
                    }
                    catch
                    {
                        row["db_owner"] = false;
                    }
                }
                else
                {
                    row["db_owner"] = false;
                }
            }

            Console.WriteLine(OutputFormatter.ConvertDataTable(allDatabases));
            Logger.Success($"Retrieved {allDatabases.Rows.Count} database(s).");

            // Flag trustworthy databases owned by sysadmin where current user has db_owner
            // This combination enables privilege escalation via EXECUTE AS OWNER
            var exploitable = allDatabases.AsEnumerable().Where(row =>
                row["Trustworthy"] != DBNull.Value && Convert.ToBoolean(row["Trustworthy"]) &&
                row["OwnerIsSysadmin"] != DBNull.Value && Convert.ToBoolean(row["OwnerIsSysadmin"]) &&
                row["db_owner"] != DBNull.Value && Convert.ToBoolean(row["db_owner"])
            ).ToList();

            if (exploitable.Any())
            {
                Logger.NewLine();
                foreach (var row in exploitable)
                {
                    Logger.Warning($"Database '{row["name"]}' is TRUSTWORTHY with sysadmin owner '{row["Owner"]}' and current user has db_owner role");
                    Logger.WarningNested($"privilege escalation possible via EXECUTE AS OWNER");
                    Logger.NewLine();
                }
            }

            return allDatabases;
        }
    }
}
