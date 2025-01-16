using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;

namespace MSSQLand.Actions.Execution
{
    public class Query : BaseAction
    {
        private string _query;

        /// <summary>
        /// Validates the additional argument provided for the query action.
        /// </summary>
        /// <param name="additionalArguments">The SQL query to validate.</param>
        public override void ValidateArguments(string additionalArguments)
        {
            if (string.IsNullOrEmpty(additionalArguments))
            {
                throw new ArgumentException("Query action requires a valid SQL query as an additional argument.");
            }

            _query = additionalArguments;
        }


        /// <summary>
        /// Executes the query action using the provided ConnectionManager.
        /// </summary>
        /// <param name="databaseContext">The ConnectionManager for executing the query.</param>
        public override void Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Executing: {_query}");
            DataTable resultTable = databaseContext.QueryService.ExecuteTable(_query);

            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(resultTable));
        }
    }
}
