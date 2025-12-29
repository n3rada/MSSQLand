using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace MSSQLand.Actions.Execution
{
    public class Query : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "T-SQL query to execute")]
        protected string _query;

        /// <summary>
        /// Validates the additional argument provided for the query action.
        /// </summary>
        /// <param name="args">The SQL query to validate.</param>
        public override void ValidateArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                throw new ArgumentException("Query action requires a valid SQL query as an additional argument.");
            }

            _query = string.Join(" ", args);
        }


        /// <summary>
        /// Executes the query action using the provided ConnectionManager.
        /// </summary>
        /// <param name="databaseContext">The ConnectionManager for executing the query.</param>
        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Executing against {databaseContext.QueryService.ExecutionServer.Hostname}: {_query}");

            try
            {
                // Detect the type of SQL command
                if (IsNonQuery(_query))
                {
                    Logger.TaskNested("Executing as a non-query command");
                    // Use ExecuteNonQuery for commands that don't return a result set
                    int rowsAffected = databaseContext.QueryService.ExecuteNonProcessing(_query);
                    if (rowsAffected >= 0)
                        Logger.Info($"Query executed successfully. Rows affected: {rowsAffected}");
                    return null;
                }

                // Use ExecuteTable for commands that return a result set
                DataTable resultTable = databaseContext.QueryService.ExecuteTable(_query);

                Logger.Success($"Query executed successfully.");
                Logger.SuccessNested($"Rows returned: {resultTable.Rows.Count}");

                if (resultTable == null || resultTable.Rows.Count == 0)
                {
                    return resultTable;
                }

                Console.WriteLine(OutputFormatter.ConvertDataTable(resultTable));
                return resultTable;
            }
            catch (SqlException sqlEx)
            {
                Logger.Error($"SQL Error: {sqlEx.Message}");
                Logger.TraceNested($"Error Number: {sqlEx.Number}");
                Logger.TraceNested($"Line Number: {sqlEx.LineNumber}");
                Logger.TraceNested($"Procedure: {sqlEx.Procedure}");
                Logger.TraceNested($"Server: {sqlEx.Server}");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"An error occurred while executing the query: {ex.Message}");
                return null;
            }
        }

        private bool IsNonQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return false;

            string[] nonQueryKeywords = { "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE", "TRUNCATE" };

            // Normalize query to remove extra spaces and handle multi-line cases
            string normalizedQuery = query.Trim().ToUpperInvariant();

            // Check if any keyword is present as a standalone word
            return nonQueryKeywords.Any(keyword =>
                normalizedQuery.Contains(keyword + " ") || normalizedQuery.Contains(keyword + ";"));
        }


    }
}
