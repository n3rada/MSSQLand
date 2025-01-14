using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;

namespace MSSQLand.Actions
{
    public class Query : BaseAction
    {
        private string _query;

        /// <summary>
        /// Validates the additional argument provided for the query action.
        /// </summary>
        /// <param name="additionalArgument">The SQL query to validate.</param>
        public override void ValidateArguments(string additionalArgument)
        {
            if (string.IsNullOrEmpty(additionalArgument))
            {
                throw new ArgumentException("Query action requires a valid SQL query as an additional argument.");
            }

            _query = additionalArgument;
        }


        /// <summary>
        /// Executes the query action using the provided ConnectionManager.
        /// </summary>
        /// <param name="connectionManager">The ConnectionManager for executing the query.</param>
        public override void Execute(DatabaseContext connectionManager)
        {
            Logger.TaskNested($"Executing: {_query}");
            DataTable resultTable = connectionManager.QueryService.ExecuteTable(_query);
              
        
            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(resultTable));

        }
    }
}
