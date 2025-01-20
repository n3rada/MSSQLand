using MSSQLand.Services;
using MSSQLand.Utilities;
using System;

namespace MSSQLand.Actions.Database
{
    internal class Procedures : BaseAction
    {
        public override void ValidateArguments(string additionalArguments)
        {
            // No additional arguments needed
        }

        public override void Execute(DatabaseContext databaseContext)
        {

            Logger.NewLine();
            Logger.Info("All stored procedures in the database:");
            string query = @"
            SELECT 
                schema_name = SCHEMA_NAME(schema_id),
                procedure_name = name,
                create_date,
                modify_date
            FROM sys.procedures
            ORDER BY modify_date DESC;";
            Console.WriteLine(MarkdownFormatter.ConvertSqlDataReaderToMarkdownTable(databaseContext.QueryService.Execute(query)));


        }
    }
}
