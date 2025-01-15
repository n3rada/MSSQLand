using MSSQLand.Services;
using MSSQLand.Utilities;
using System;

namespace MSSQLand.Actions.Database
{
    internal class Databases : BaseAction
    {
        public override void ValidateArguments(string additionalArgument)
        {
            // No additional arguments needed
        }
        

        public override void Execute(DatabaseContext connectionManager)
        {
            Logger.Info("Databases on the database management system (DBMS)");


            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(connectionManager.QueryService.ExecuteTable("SELECT dbid, name, crdate, filename FROM master.dbo.sysdatabases ORDER BY crdate DESC;")));

        }
    }
}
