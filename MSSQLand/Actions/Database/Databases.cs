using MSSQLand.Services;
using MSSQLand.Utilities;
using System;

namespace MSSQLand.Actions.Database
{
    internal class Databases : BaseAction
    {
        public override void ValidateArguments(string additionalArguments)
        {
            // No additional arguments needed
        }
        

        public override void Execute(DatabaseContext databaseContext)
        {
            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(databaseContext.QueryService.ExecuteTable("SELECT dbid, name, crdate, filename FROM master.dbo.sysdatabases ORDER BY crdate DESC;")));
        }
    }
}
