using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;


namespace MSSQLand.Actions.Database
{
    internal class Links : BaseAction
    {


        public override void ValidateArguments(string additionalArgument)
        {
            // No additional arguments needed
        }


        /// <summary>
        /// Executes the query action using the provided ConnectionManager.
        /// </summary>
        /// <param name="connectionManager">The ConnectionManager for executing the query.</param>
        public override void Execute(DatabaseContext connectionManager)
        {
            Logger.TaskNested($"Retrieving Linked SQL Servers");
            DataTable resultTable = connectionManager.QueryService.ExecuteTable("SELECT srv.name AS [Linked Server], srv.product, srv.provider, srv.data_source, COALESCE(prin.name, 'N/A') AS [Local Login], ll.uses_self_credential AS [Is Self Mapping], ll.remote_name AS [Remote Login] FROM sys.servers srv LEFT JOIN sys.linked_logins ll ON srv.server_id = ll.server_id LEFT JOIN sys.server_principals prin ON ll.local_principal_id = prin.principal_id WHERE srv.is_linked = 1;");


            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(resultTable));

        }
    }
}
