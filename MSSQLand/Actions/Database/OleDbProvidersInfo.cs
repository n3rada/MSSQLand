using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;


namespace MSSQLand.Actions.Database
{
    internal class OleDbProvidersInfo : BaseAction
    {
        public override void ValidateArguments(string[] args)
        {
            // No additional arguments needed
        }

        /// <summary>
        /// https://github.com/NetSPI/PowerUpSQL/blob/7d73373b0751b8648a800fbeef4c00ced66eba58/PowerUpSQL.ps1#L6987
        /// </summary>
        /// <param name="databaseContext"></param>
        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Retrieving OLE DB providers information");
            
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

            DECLARE MY_CURSOR1 CURSOR
            FOR
            SELECT * FROM #Providers
            OPEN MY_CURSOR1
            FETCH NEXT FROM MY_CURSOR1 INTO @Provider_name,@Provider_parse_name,@Provider_description
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

	            FETCH NEXT FROM MY_CURSOR1 INTO  @Provider_name,@Provider_parse_name,@Provider_description

	            END   

            SELECT * FROM #ProviderInformation

            CLOSE MY_CURSOR1
            DEALLOCATE MY_CURSOR1
            DROP TABLE #Providers
            DROP TABLE #ProviderInformation";


            var result = databaseContext.QueryService.ExecuteTable(query);
            Console.WriteLine(OutputFormatter.ConvertDataTable(result));
            
            Logger.Success($"Retrieved {result.Rows.Count} OLE DB provider(s)");

            return null;

        }
    }
}
