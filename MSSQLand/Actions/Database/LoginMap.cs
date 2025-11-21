using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;
using System.Linq;

namespace MSSQLand.Actions.Database
{
    /// <summary>
    /// Maps server logins to database users across all accessible databases.
    /// 
    /// Shows which server-level principals (logins) can access which databases
    /// and what database user they are mapped to. This is critical for understanding:
    /// - Cross-database access patterns
    /// - Orphaned users (database users without corresponding logins)
    /// - Login-to-user name mismatches
    /// - Actual database access vs. HAS_DBACCESS permissions
    /// 
    /// Usage:
    /// - No argument: Show all login-to-user mappings
    /// - With login name: Show mappings only for specified server login
    /// 
    /// Note: Only shows mappings for databases where you have access.
    /// </summary>
    internal class LoginMap : BaseAction
    {
        [ArgumentMetadata(Position = 0, Description = "Optional: Server login name to filter mappings")]
        private string? LoginFilter;

        public override void ValidateArguments(string additionalArguments)
        {
            if (!string.IsNullOrWhiteSpace(additionalArguments))
            {
                LoginFilter = additionalArguments.Trim();
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            bool isAzureSQL = databaseContext.QueryService.IsAzureSQL();

            if (isAzureSQL)
            {
                Logger.Warning("Login-to-user mapping not available on Azure SQL Database (PaaS)");
                Logger.WarningNested("Azure SQL Database uses contained database users");
                return null;
            }

            string filterInfo = string.IsNullOrWhiteSpace(LoginFilter) 
                ? "all logins" 
                : $"login '{LoginFilter}'";
            Logger.Task($"Mapping server logins to database users across all accessible databases ({filterInfo})");
            Logger.NewLine();

            string query = @"
                DECLARE @loginFilter NVARCHAR(128) = " + (string.IsNullOrWhiteSpace(LoginFilter) ? "NULL" : $"'{LoginFilter.Replace("'", "''")}'") + @";
                DECLARE @mapping TABLE (
                    [Database] NVARCHAR(128),
                    [Server Login] NVARCHAR(128),
                    [Login Type] NVARCHAR(60),
                    [Database User] NVARCHAR(128),
                    [User Type] NVARCHAR(60),
                    [Has DB Access] BIT,
                    [Orphaned] BIT
                );

                DECLARE @dbname NVARCHAR(128);
                DECLARE @sql NVARCHAR(MAX);

                DECLARE db_cursor CURSOR FOR
                SELECT name FROM master.sys.databases 
                WHERE HAS_DBACCESS(name) = 1 
                AND state_desc = 'ONLINE'
                AND name NOT IN ('tempdb');

                OPEN db_cursor;
                FETCH NEXT FROM db_cursor INTO @dbname;

                WHILE @@FETCH_STATUS = 0
                BEGIN
                    SET @sql = N'USE [' + @dbname + N'];
                    INSERT INTO @mapping
                    SELECT 
                        ''' + @dbname + ''' AS [Database],
                        sp.name AS [Server Login],
                        sp.type_desc AS [Login Type],
                        dp.name AS [Database User],
                        dp.type_desc AS [User Type],
                        HAS_DBACCESS(''' + @dbname + ''') AS [Has DB Access],
                        CASE 
                            WHEN sp.sid IS NULL THEN 1
                            ELSE 0
                        END AS [Orphaned]
                    FROM sys.database_principals dp
                    LEFT JOIN master.sys.server_principals sp ON dp.sid = sp.sid
                    WHERE dp.type IN (''S'', ''U'', ''G'', ''E'', ''X'')
                    AND dp.name NOT LIKE ''##%''
                    AND dp.name NOT IN (''guest'', ''INFORMATION_SCHEMA'', ''sys'')
                    AND (@loginFilter IS NULL OR sp.name = @loginFilter OR dp.name = @loginFilter)';  -- Filter by login or user name
                    
                    BEGIN TRY
                        EXEC sp_executesql @sql;
                    END TRY
                    BEGIN CATCH
                        -- Skip databases where we don't have permission
                    END CATCH
                    
                    FETCH NEXT FROM db_cursor INTO @dbname;
                END;

                CLOSE db_cursor;
                DEALLOCATE db_cursor;

                SELECT * FROM @mapping
                ORDER BY [Orphaned] DESC, [Database], [Server Login];";

            try
            {
                DataTable results = databaseContext.QueryService.ExecuteTable(query);

                if (results.Rows.Count == 0)
                {
                    string msg = string.IsNullOrWhiteSpace(LoginFilter)
                        ? "No login-to-user mappings found"
                        : $"No mappings found for login '{LoginFilter}'";
                    Logger.Warning(msg);
                    return null;
                }

                // Sort by security importance: orphaned users first, then by login privilege
                var sortedRows = results.AsEnumerable()
                    .OrderByDescending(row => Convert.ToBoolean(row["Orphaned"]))
                    .ThenBy(row => row["Database"].ToString())
                    .ThenBy(row => row["Server Login"].ToString());

                DataTable sortedResults = sortedRows.CopyToDataTable();

                // Count statistics
                int totalMappings = sortedResults.Rows.Count;
                int orphanedUsers = sortedResults.AsEnumerable().Count(r => Convert.ToBoolean(r["Orphaned"]));
                int mismatchedNames = sortedResults.AsEnumerable()
                    .Count(r => !Convert.ToBoolean(r["Orphaned"]) && 
                                r["Server Login"].ToString() != r["Database User"].ToString());

                Console.WriteLine(OutputFormatter.ConvertDataTable(sortedResults));

                Logger.NewLine();
                Logger.Info($"Total mappings: {totalMappings}");
                
                if (orphanedUsers > 0)
                {
                    Logger.Warning($"Orphaned users (no login): {orphanedUsers}");
                }
                
                if (mismatchedNames > 0)
                {
                    Logger.Info($"Name mismatches (login ≠ user): {mismatchedNames}");
                }

                return sortedResults;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error mapping logins to users: {ex.Message}");
                return null;
            }
        }
    }
}
