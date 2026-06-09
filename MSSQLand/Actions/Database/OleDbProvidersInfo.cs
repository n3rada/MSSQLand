// MSSQLand/Actions/Database/OleDbProvidersInfo.cs

using System;

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;


namespace MSSQLand.Actions.Database
{
    internal class OleDbProvidersInfo : BaseAction
    {
        /// <summary>
        /// Enumerates all OLE DB providers registered on the SQL Server instance and reads
        /// their configuration from the registry via <c>sys.xp_instance_regread</c>.
        /// Requires execute permission on <c>xp_enum_oledb_providers</c> and <c>xp_instance_regread</c>.
        /// <para>
        /// Retrieved settings per provider: AllowInProcess, DisallowAdHocAccess, DynamicParameters,
        /// IndexAsAccessPath, LevelZeroOnly, NestedQueries, NonTransactedUpdates, SqlServerLIKE.
        /// </para>
        /// Reference: https://github.com/NetSPI/PowerUpSQL/blob/7d73373b0751b8648a800fbeef4c00ced66eba58/PowerUpSQL.ps1#L6987
        /// </summary>
        /// <param name="databaseContext"></param>
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.Task("Retrieving OLE DB providers information");

            // Query enumerates OLE DB providers and reads their registry settings
            // Uses cursor to iterate through each provider and read configuration from registry
            // Settings include AllowInProcess, DisallowAdHocAccess, DynamicParameters, etc.
            string query = @"
			CREATE TABLE #Providers ([ProviderName] varchar(8000),
            [ParseName] varchar(8000),
            [ProviderDescription] varchar(8000))

            INSERT INTO #Providers
            EXEC xp_enum_oledb_providers

            CREATE TABLE #ProviderInformation ([ProviderName] varchar(8000),
            [ProviderDescription] varchar(8000),
            [ProviderParseName] varchar(8000),
            [AllowInProcess] int,
            [DisallowAdHocAccess] int,
            [DynamicParameters] int,
            [IndexAsAccessPath] int,
            [LevelZeroOnly] int,
            [NestedQueries] int,
            [NonTransactedUpdates] int,
            [SqlServerLIKE] int)

            DECLARE @Provider_name varchar(8000);
            DECLARE @Provider_parse_name varchar(8000);
            DECLARE @Provider_description varchar(8000);
            DECLARE @property_name varchar(8000)
            DECLARE @regpath nvarchar(512)

            DECLARE cur_OleDbProviders CURSOR
            FOR
            SELECT * FROM #Providers
            OPEN cur_OleDbProviders
            FETCH NEXT FROM cur_OleDbProviders INTO @Provider_name,@Provider_parse_name,@Provider_description
            WHILE @@FETCH_STATUS = 0

	            BEGIN

	            SET @regpath = N'SOFTWARE\Microsoft\MSSQLServer\Providers\' + @provider_name

	             DECLARE @AllowInProcess int
	             SET @AllowInProcess = 0
	             exec sys.xp_instance_regread N'HKEY_LOCAL_MACHINE',@regpath,'AllowInProcess',	@AllowInProcess OUTPUT
	             IF @AllowInProcess IS NULL
	             SET @AllowInProcess = 0

	             DECLARE @DisallowAdHocAccess int
	             SET @DisallowAdHocAccess = 0
	             exec sys.xp_instance_regread N'HKEY_LOCAL_MACHINE',@regpath,'DisallowAdHocAccess',	@DisallowAdHocAccess OUTPUT
	             IF @DisallowAdHocAccess IS NULL
	             SET @DisallowAdHocAccess = 0

	             DECLARE @DynamicParameters  int
	             SET @DynamicParameters  = 0
	             exec sys.xp_instance_regread N'HKEY_LOCAL_MACHINE',@regpath,'DynamicParameters',	@DynamicParameters OUTPUT
	             IF @DynamicParameters  IS NULL
	             SET @DynamicParameters  = 0

	             DECLARE @IndexAsAccessPath int
	             SET @IndexAsAccessPath = 0
	             exec sys.xp_instance_regread N'HKEY_LOCAL_MACHINE',@regpath,'IndexAsAccessPath',	@IndexAsAccessPath OUTPUT
	             IF @IndexAsAccessPath IS NULL
	             SET @IndexAsAccessPath  = 0

	             DECLARE @LevelZeroOnly int
	             SET @LevelZeroOnly  = 0
	             exec sys.xp_instance_regread N'HKEY_LOCAL_MACHINE',@regpath,'LevelZeroOnly',	@LevelZeroOnly OUTPUT
	             IF  @LevelZeroOnly IS NULL
	             SET  @LevelZeroOnly  = 0

	             DECLARE @NestedQueries int
	             SET @NestedQueries = 0
	             exec sys.xp_instance_regread N'HKEY_LOCAL_MACHINE',@regpath,'NestedQueries',	@NestedQueries OUTPUT
	             IF   @NestedQueries IS NULL
	             SET  @NestedQueries = 0

	             DECLARE @NonTransactedUpdates int
	             SET @NonTransactedUpdates = 0
	             exec sys.xp_instance_regread N'HKEY_LOCAL_MACHINE',@regpath,'NonTransactedUpdates',	@NonTransactedUpdates  OUTPUT
	             IF  @NonTransactedUpdates IS NULL
	             SET @NonTransactedUpdates = 0

	             DECLARE @SqlServerLIKE int
	             SET @SqlServerLIKE  = 0
	             exec sys.xp_instance_regread N'HKEY_LOCAL_MACHINE',@regpath,'SqlServerLIKE',	@SqlServerLIKE OUTPUT
	             IF  @SqlServerLIKE IS NULL
	             SET @SqlServerLIKE = 0

	            INSERT INTO #ProviderInformation
	            VALUES (@Provider_name,@Provider_description,@Provider_parse_name,@AllowInProcess,@DisallowAdHocAccess,@DynamicParameters,@IndexAsAccessPath,@LevelZeroOnly,@NestedQueries,@NonTransactedUpdates,@SqlServerLIKE);

	            FETCH NEXT FROM cur_OleDbProviders INTO  @Provider_name,@Provider_parse_name,@Provider_description

	            END

            SELECT * FROM #ProviderInformation

            CLOSE cur_OleDbProviders
            DEALLOCATE cur_OleDbProviders
            DROP TABLE #Providers
            DROP TABLE #ProviderInformation";


            var result = databaseContext.QueryService.ExecuteTable(query);
            Console.WriteLine(OutputFormatter.ConvertDataTable(result));

            Logger.Success($"Retrieved {result.Rows.Count} OLE DB provider(s)");

            return null;

        }
    }
}
