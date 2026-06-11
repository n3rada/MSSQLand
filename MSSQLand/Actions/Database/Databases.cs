// MSSQLand/Actions/Database/Databases.cs

using System;
using System.Data;
using System.Linq;

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;

namespace MSSQLand.Actions.Database
{
    /// <summary>
    /// List all available databases with accessibility, trustworthiness, owner, and file paths.
    /// </summary>
    internal class Databases : BaseAction
    {
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.Task("Enumerating databases");

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
                    ORDER BY HAS_DBACCESS(d.name) DESC, d.name ASC"
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
                    ORDER BY HAS_DBACCESS(d.name) DESC, d.name ASC"
                );
            }

            // Check db_owner role membership for all accessible databases in one roundtrip
            // IS_MEMBER is context-dependent so we build dynamic SQL with USE per-database
            // EXECUTE() wraps the batch so USE statements don't change the outer context
            allDatabases.Columns.Add("db_owner", typeof(bool));
            allDatabases.Columns["db_owner"].SetOrdinal(allDatabases.Columns["OwnerIsSysadmin"].Ordinal + 1);

            try
            {
                DataTable ownerResults = databaseContext.QueryService.ExecuteTable(
                    @"DECLARE @sql NVARCHAR(MAX) = N'';
SELECT @sql = @sql +
    N'USE [' + REPLACE(name, ']', ']]') + N']; INSERT INTO #db_owner_check VALUES(''' + REPLACE(name, '''', '''''') + N''', CAST(IS_MEMBER(''db_owner'') AS BIT)); '
FROM sys.databases WHERE HAS_DBACCESS(name) = 1;
CREATE TABLE #db_owner_check (db_name NVARCHAR(256), is_db_owner BIT);
EXECUTE(@sql);
SELECT db_name, is_db_owner FROM #db_owner_check;
DROP TABLE #db_owner_check"
                );

                var ownerMap = ownerResults.AsEnumerable().ToDictionary(
                    r => r["db_name"].ToString(),
                    r => r["is_db_owner"] != DBNull.Value && Convert.ToBoolean(r["is_db_owner"])
                );

                foreach (DataRow row in allDatabases.Rows)
                {
                    string dbName = row["name"].ToString();
                    row["db_owner"] = ownerMap.ContainsKey(dbName) && ownerMap[dbName];
                }
            }
            catch
            {
                foreach (DataRow row in allDatabases.Rows)
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
