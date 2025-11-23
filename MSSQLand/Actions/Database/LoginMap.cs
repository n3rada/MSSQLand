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
        private string? LoginFilter = null;

        public override void ValidateArguments(string[] args)
        {
            BindArgumentsToFields(args);
            // If positional value wasn't used, leave LoginFilter null
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

            Logger.TaskNested("Mapping server logins to database users across all accessible databases");

            string query = @"
                DECLARE @mapping TABLE (
                    [Database] NVARCHAR(128),
                    [Server Login] NVARCHAR(128),
                    [Login Type] NVARCHAR(60),
                    [Database User] NVARCHAR(128),
                    [User Type] NVARCHAR(60),
                    [Effective Access Via] NVARCHAR(128),
                    [Orphaned] BIT
                );

                DECLARE @dbname NVARCHAR(128);
                DECLARE @sql NVARCHAR(MAX);

                DECLARE db_cursor CURSOR FOR
                SELECT name FROM master.sys.databases 
                WHERE HAS_DBACCESS(name) = 1 
                AND state_desc = 'ONLINE'
                AND name NOT IN ('tempdb', 'model');

                OPEN db_cursor;
                FETCH NEXT FROM db_cursor INTO @dbname;

                WHILE @@FETCH_STATUS = 0
                BEGIN
                    SET @sql = N'
                    SELECT 
                        ''' + @dbname + ''' AS [Database],
                        ISNULL(sp.name, ''<Orphaned>'') AS [Server Login],
                        ISNULL(sp.type_desc, ''N/A'') AS [Login Type],
                        dp.name AS [Database User],
                        dp.type_desc AS [User Type],
                        CASE 
                            -- Check if there''s a different login token entry that grants access
                            WHEN sp.sid IS NOT NULL AND sp.name != dp.name 
                                AND EXISTS (
                                    SELECT 1 FROM master.sys.login_token lt
                                    WHERE lt.sid = dp.sid AND lt.type = ''WINDOWS GROUP''
                                )
                            THEN (
                                SELECT TOP 1 lt.name 
                                FROM master.sys.login_token lt
                                WHERE lt.sid = dp.sid AND lt.type = ''WINDOWS GROUP''
                            )
                            WHEN sp.sid IS NOT NULL THEN ''Direct''
                            ELSE NULL
                        END AS [Effective Access Via],
                        CASE 
                            WHEN dp.name = ''guest'' THEN 0
                            WHEN sp.sid IS NULL THEN 1
                            ELSE 0
                        END AS [Orphaned]
                    FROM [' + @dbname + '].sys.database_principals dp
                    LEFT JOIN master.sys.server_principals sp ON dp.sid = sp.sid
                    WHERE dp.type IN (''S'', ''U'', ''G'', ''E'', ''X'')
                    AND dp.name NOT LIKE ''##%''
                    AND dp.name NOT IN (''INFORMATION_SCHEMA'', ''sys'', ''guest'')';
                    
                    BEGIN TRY
                        INSERT INTO @mapping
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
                    Logger.Warning("No login-to-user mappings found");
                    return null;
                }

                // Apply C# filtering if login filter specified
                var filteredRows = results.AsEnumerable();
                
                if (!string.IsNullOrWhiteSpace(LoginFilter))
                {
                    filteredRows = filteredRows.Where(row => 
                        row["Server Login"].ToString().Equals(LoginFilter, StringComparison.OrdinalIgnoreCase) ||
                        row["Database User"].ToString().Equals(LoginFilter, StringComparison.OrdinalIgnoreCase)
                    );
                    
                    if (!filteredRows.Any())
                    {
                        Logger.Warning($"No mappings found for login '{LoginFilter}'");
                        return null;
                    }
                    
                    Logger.Info($"Filtered for login: '{LoginFilter}'");
                    Logger.NewLine();
                }

                // Sort by security importance: orphaned users first, then by database
                var sortedRows = filteredRows
                    .OrderByDescending(row => Convert.ToBoolean(row["Orphaned"]))
                    .ThenBy(row => row["Database"].ToString())
                    .ThenBy(row => row["Server Login"].ToString());

                DataTable sortedResults = sortedRows.CopyToDataTable();

                Console.WriteLine(OutputFormatter.ConvertDataTable(sortedResults));

                Logger.Success("Login-to-user mapping completed");

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
