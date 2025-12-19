// MSSQLand/Actions/Remote/Links.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;


namespace MSSQLand.Actions.Remote
{
    internal class Links : BaseAction
    {


        public override void ValidateArguments(string[] args)
        {
            // No additional arguments needed
        }


        /// <summary>
        /// Executes the query action using the provided ConnectionManager.
        /// </summary>
        /// <param name="databaseContext">The ConnectionManager for executing the query.</param>
        public override object? Execute(DatabaseContext databaseContext)
        {
            // Check if running on Azure SQL Database (PaaS)
            if (databaseContext.QueryService.IsAzureSQL())
            {
                Logger.Warning("Linked servers aren't available in Azure SQL Database (PaaS).");
                Logger.WarningNested("Linked servers are supported in Azure SQL Managed Instance.");
                Logger.WarningNested("https://learn.microsoft.com/en-us/sql/relational-databases/linked-servers/linked-servers-database-engine");
                Logger.WarningNested("For Azure SQL Database, use /a:extsources to check External Data Sources.");
                return null;
            }

            Logger.TaskNested($"Retrieving Linked SQL Servers");

            DataTable resultTable = GetLinkedServers(databaseContext);

            if (resultTable.Rows.Count == 0)
            {
                Logger.Warning("No linked servers found.");
            }
            else
            {
                Console.WriteLine(OutputFormatter.ConvertDataTable(resultTable));
            }

            return resultTable;
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
                    prin.name AS [Local Login],
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
