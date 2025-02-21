using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;


namespace MSSQLand.Actions.Database
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
            Logger.TaskNested($"Retrieving Linked SQL Servers");
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
                FROM sys.servers srv
                LEFT JOIN sys.linked_logins ll ON srv.server_id = ll.server_id
                LEFT JOIN sys.server_principals prin ON ll.local_principal_id = prin.principal_id
                WHERE srv.is_linked = 1
                ORDER BY srv.modify_date DESC;";
            DataTable resultTable = databaseContext.QueryService.ExecuteTable(query);


            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(resultTable));

            return resultTable;

        }
    }
}
