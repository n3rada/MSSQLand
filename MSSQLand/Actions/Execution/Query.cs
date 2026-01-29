// MSSQLand/Actions/Execution/Query.cs

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
        [ArgumentMetadata(Position = 0, Required = true, CaptureRemaining = true, Description = "T-SQL query to execute")]
        protected string _query;

        [ArgumentMetadata(LongName = "all", Description = "Execute query across all accessible databases")]
        private bool _executeAll = false;

        /// <summary>
        /// Executes the query action using the provided ConnectionManager.
        /// </summary>
        /// <param name="databaseContext">The ConnectionManager for executing the query.</param>
        public override object Execute(DatabaseContext databaseContext)
        {
            if (_executeAll)
            {
                return ExecuteAcrossAllDatabases(databaseContext);
            }

            Logger.TaskNested($"Executing against {databaseContext.QueryService.ExecutionServer.Hostname}: {_query}");
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
        /// </summary>
        /// <param name="databaseContext">The database context.</param>
        /// <param name="query">The query to execute.</param>
        /// <returns>DataTable with results, or empty DataTable for non-query commands.</returns>
        private DataTable ExecuteOn(DatabaseContext databaseContext, string query)
        {
            try
            {
                // Execute query - SQL Server handles both queries and non-queries
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

                // Provide guidance for common distributed query errors
                if (sqlEx.Number == 9514) // XML data type not supported
                {
                    Logger.Warning("XML columns are not supported in distributed queries (EXEC AT / OPENQUERY).");
                    Logger.WarningNested("Use explicit column list and CAST XML columns: CAST([XmlCol] AS NVARCHAR(MAX))");
                }

                throw;
            }
            catch (Exception ex)
            {
                Logger.Error($"An error occurred while executing the query: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Executes the query across all accessible databases.
        /// First tries sp_MSforeachdb, falls back to manual loop if it fails.
        /// </summary>
        private object ExecuteAcrossAllDatabases(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Executing across ALL accessible databases on {databaseContext.QueryService.ExecutionServer.Hostname}");

            // Try sp_MSforeachdb first
            try
            {
                Logger.Info("Attempting execution via sp_MSforeachdb");
                return ExecuteWithMSForeachDb(databaseContext);
            }
            catch (Exception ex)
            {
                Logger.WarningNested($"sp_MSforeachdb failed: {ex.Message}");
                Logger.Info("Falling back to manual loop across databases");
                return ExecuteWithManualLoop(databaseContext);
            }
        }

        /// <summary>
        /// Executes query using sp_MSforeachdb for better performance.
        /// </summary>
        private object ExecuteWithMSForeachDb(DatabaseContext databaseContext)
        {
            // Build query with sp_MSforeachdb
            // Note: ? is replaced with database name by sp_MSforeachdb
            string foreachQuery = $@"
                EXEC sp_MSforeachdb '
                    USE [?];
                    IF HAS_DBACCESS(DB_NAME()) = 1
                    BEGIN
                        SELECT DB_NAME() AS [Database], * FROM (
                            {_query.Replace("'", "''")}
                        ) AS QueryResults
                    END
                '";

            DataTable resultTable = databaseContext.QueryService.ExecuteTable(foreachQuery);

            Logger.Success($"Query executed successfully via sp_MSforeachdb.");
            Logger.SuccessNested($"Total rows returned: {resultTable.Rows.Count}");

            if (resultTable == null || resultTable.Rows.Count == 0)
            {
                Logger.Info("No rows returned from any database.");
                return resultTable;
            }

            Console.WriteLine(OutputFormatter.ConvertDataTable(resultTable));
            return resultTable;
        }

        /// <summary>
        /// Executes query by manually looping through accessible databases.
        /// </summary>
        private object ExecuteWithManualLoop(DatabaseContext databaseContext)
        {
            // Get list of accessible databases
            DataTable databases = databaseContext.QueryService.ExecuteTable(
                "SELECT name FROM master.sys.databases WHERE HAS_DBACCESS(name) = 1 AND state = 0 ORDER BY name"
            );

            if (databases.Rows.Count == 0)
            {
                Logger.Warning("No accessible databases found.");
                return null;
            }

            Logger.Info($"Found {databases.Rows.Count} accessible database(s): {string.Join(", ", databases.AsEnumerable().Select(row => row["name"].ToString()))}");

            // Combined results
            DataTable combinedResults = null;
            int totalRows = 0;

            // Iterate through each database
            foreach (DataRow dbRow in databases.Rows)
            {
                string dbName = dbRow["name"].ToString();
                Logger.TaskNested($"Querying: {dbName}");

                try
                {
                    // Build query with database context
                    string dbQuery = $"USE [{dbName}]; {_query}";

                    // Execute query using ExecuteOn
                    DataTable dbResults = ExecuteOn(databaseContext, dbQuery);

                    // Merge results if it's a DataTable
                    if (dbResults != null && dbResults.Rows.Count > 0)
                    {
                        if (combinedResults == null)
                        {
                            // Initialize combined results with schema + Database column
                            combinedResults = dbResults.Clone();
                            combinedResults.Columns.Add("Database", typeof(string));
                            combinedResults.Columns["Database"].SetOrdinal(0); // Make it the first column
                        }

                        // Add rows with database name
                        foreach (DataRow row in dbResults.Rows)
                        {
                            DataRow newRow = combinedResults.NewRow();

                            // Copy all original columns
                            for (int i = 0; i < dbResults.Columns.Count; i++)
                            {
                                newRow[i + 1] = row[i]; // Offset by 1 because Database is first
                            }

                            // Set the database name in the first column
                            newRow["Database"] = dbName;

                            combinedResults.Rows.Add(newRow);
                        }

                        totalRows += dbResults.Rows.Count;
                    }
                }
                catch (Exception ex)
                {
                    Logger.WarningNested($"Error: {ex.Message}");
                    continue;
                }
            }

            Logger.NewLine();
            Logger.Success($"Total rows from all databases: {totalRows}");

            if (combinedResults == null || combinedResults.Rows.Count == 0)
            {
                Logger.Info("No rows returned from any database.");
                return combinedResults;
            }

            Logger.Info("Combined results");
            Console.WriteLine(OutputFormatter.ConvertDataTable(combinedResults));

            return combinedResults;
        }
    }
}
