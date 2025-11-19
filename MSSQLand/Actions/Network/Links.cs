using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;


namespace MSSQLand.Actions.Network
{
    internal class Links : BaseAction
    {


        public override void ValidateArguments(string additionalArguments)
        {
            // No additional arguments needed
        }


        /// <summary>
        /// Executes the query action using the provided ConnectionManager.
        /// </summary>
        /// <param name="databaseContext">The ConnectionManager for executing the query.</param>
        public override object? Execute(DatabaseContext databaseContext)
        {
            // Check if running on Azure SQL Database
            if (databaseContext.QueryService.IsAzureSQL())
            {
                Logger.Warning("Linked servers aren't available in Azure SQL Database.");
                Logger.NestedWarning("https://learn.microsoft.com/en-us/sql/relational-databases/linked-servers/linked-servers-database-engine");
    
                Logger.Info("Checking for External Data Sources");
                Logger.InfoNested("Azure SQL Database uses External Data Sources instead of traditional linked servers");
                Logger.NewLine();

                DataTable externalSources = GetAzureExternalDataSources(databaseContext);
                
                if (externalSources.Rows.Count == 0)
                {
                    Logger.Warning("No external data sources found");
                }
                else
                {
                    Console.WriteLine(OutputFormatter.ConvertDataTable(externalSources));
                }
                
                return externalSources;
            }

            Logger.TaskNested($"Retrieving Linked SQL Servers");

            DataTable resultTable = GetLinkedServers(databaseContext);

            Console.WriteLine(OutputFormatter.ConvertDataTable(resultTable));

            return resultTable;

        }


        /// <summary>
        /// Retrieves external data sources on Azure SQL Database (Elastic Query).
        /// </summary>
        private static DataTable GetAzureExternalDataSources(DatabaseContext databaseContext)
        {
            string query = @"
                SELECT
                    name AS [Name],
                    type_desc AS [Type],
                    location AS [Location],
                    database_name AS [Database Name],
                    credential_name AS [Credential]
                FROM sys.external_data_sources
                ORDER BY name;";

            return databaseContext.QueryService.ExecuteTable(query);
        }


        /// <summary>
        /// Retrieves linked servers and login mappings.
        /// </summary>
        public static DataTable GetLinkedServers(DatabaseContext databaseContext)
        {
            string query = @"
                SELECT
                    srv.modify_date AS [Last Modified],
                    srv.name AS [Link],
                    srv.product AS [Product],
                    srv.provider AS [Provider],
                    srv.data_source AS [Data Source],
                    COALESCE(prin.name, 'N/A') AS [Local Login],
                    ll.remote_name AS [Remote Login],
                    srv.is_rpc_out_enabled AS [RPC Out],
                    srv.is_data_access_enabled AS [OPENQUERY],
                    srv.is_collation_compatible AS [Collation]
                FROM master.sys.servers srv
                LEFT JOIN master.sys.linked_logins ll ON srv.server_id = ll.server_id
                LEFT JOIN master.sys.server_principals prin ON ll.local_principal_id = prin.principal_id
                WHERE srv.is_linked = 1
                ORDER BY srv.modify_date DESC;";

            return databaseContext.QueryService.ExecuteTable(query);
        }
    }
}
