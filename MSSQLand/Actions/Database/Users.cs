using MSSQLand.Services;
using MSSQLand.Utilities;
using System;


namespace MSSQLand.Actions.Database
{
    internal class Users : BaseAction
    {
        public override void ValidateArguments(string additionalArguments)
        {
            // No additional arguments needed
        }

        public override void Execute(DatabaseContext connectionManager)
        {
            Logger.Info("Database users");


            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(connectionManager.QueryService.ExecuteTable("SELECT name AS username, create_date, modify_date, type_desc AS type, authentication_type_desc AS authentication_type FROM sys.database_principals WHERE type NOT IN ('A', 'R', 'X') AND sid IS NOT null AND name NOT LIKE '##%' ORDER BY modify_date DESC;")));

            Logger.Info("Server principals");

            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(connectionManager.QueryService.ExecuteTable("SELECT name, type_desc, is_disabled, create_date, modify_date FROM sys.server_principals WHERE name NOT LIKE '##%' ORDER BY modify_date DESC;")));


        }
    }
}
