// MSSQLand/Actions/Execution/Query.cs

using System;
using System.Data;
using System.Data.SqlClient;

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;

namespace MSSQLand.Actions.Execution
{
    public class Query : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "T-SQL query to execute")]
        protected string _query;

        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.Info($"Executing against {databaseContext.QueryService.ExecutionServer.Hostname}: {_query}");

            Logger.NewLine();

            DataTable result = ExecuteOn(databaseContext, _query);

            Logger.Success("Query executed successfully.");

            if (result != null && result.Rows.Count > 0)
            {
                Console.WriteLine(OutputFormatter.ConvertDataTable(result));
                Logger.SuccessNested($"Total rows returned: {result.Rows.Count}");
            }

            return result;
        }

        /// <summary>
        /// Executes a query on the current database context.
        /// Protected so QueryAll can reuse it for per-database execution.
        /// </summary>
        protected DataTable ExecuteOn(DatabaseContext databaseContext, string query)
        {
            try
            {
                DataTable resultTable = databaseContext.QueryService.ExecuteTable(query);
                return resultTable;
            }
            catch (SqlException sqlEx)
            {
                Logger.Error($"SQL Error: {sqlEx.Message}");
                Logger.TraceNested($"Error Number: {sqlEx.Number}");
                Logger.TraceNested($"Line Number: {sqlEx.LineNumber}");
                Logger.TraceNested($"Procedure: {sqlEx.Procedure}");
                Logger.TraceNested($"Server: {sqlEx.Server}");

                if (sqlEx.Number == 9514)
                {
                    Logger.Warning("XML columns are not supported in distributed queries (EXEC AT / OPENQUERY).");
                    Logger.WarningNested("Use explicit column list and CAST XML columns: CAST([XmlCol] AS NVARCHAR(MAX))");
                }

                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"An error occurred while executing the query: {ex.Message}");
                throw;
            }
        }
    }
}
