using MSSQLand.Services;
using MSSQLand.Utilities;
using System;


namespace MSSQLand.Actions.Database
{
    internal class Permissions : BaseAction
    {
        public override void ValidateArguments(string additionalArgument)
        {
            // No additional arguments needed
        }

        public override void Execute(DatabaseContext connectionManager)
        {

            Logger.Info("Server permissions");


            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(connectionManager.QueryService.ExecuteTable("SELECT permission_name FROM fn_my_permissions(NULL, 'SERVER');")));

            Logger.Info("Database permissions");

            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(connectionManager.QueryService.ExecuteTable("SELECT permission_name FROM fn_my_permissions(NULL, 'DATABASE');")));

            Logger.Info("Database access");

            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(connectionManager.QueryService.ExecuteTable("SELECT name FROM sys.databases WHERE HAS_DBACCESS(name) = 1;")));


        }
    }
}
