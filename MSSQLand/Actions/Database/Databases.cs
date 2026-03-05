// MSSQLand/Actions/Database/Databases.cs

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
                CAST(HAS_DBACCESS(d.name) AS BIT) AS Accessible,
                d.is_trustworthy_on AS Trustworthy,
                d.state_desc AS [State],
                d.user_access_desc AS [Access],
                d.is_read_only AS [ReadOnly],
                d.recovery_model_desc AS [Recovery Model],
                SUSER_SNAME(d.owner_sid) AS Owner,
                d.create_date AS [Created],
                mf.physical_name AS [MDF Path]
            FROM sys.databases d
            LEFT JOIN sys.master_files mf ON d.database_id = mf.database_id AND mf.type = 0
            ORDER BY HAS_DBACCESS(d.name) DESC, d.create_date DESC;"
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
    }

    Console.WriteLine(OutputFormatter.ConvertDataTable(allDatabases));
    Logger.Success($"Retrieved {allDatabases.Rows.Count} database(s).");
    return allDatabases;
}
