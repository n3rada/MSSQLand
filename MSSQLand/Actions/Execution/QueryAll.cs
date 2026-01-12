// MSSQLand/Actions/Execution/QueryAll.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.Execution
{
    /// <summary>
    /// Executes a query across all accessible databases by manually switching context.
    /// </summary>
    public class QueryAll : Query
    {
        /// <summary>
        /// Executes the query across all databases.
        /// </summary>
        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Executing across ALL accessible databases on {databaseContext.QueryService.ExecutionServer.Hostname}");

            // Get list of accessible databases
            DataTable databases = databaseContext.QueryService.ExecuteTable(
                "SELECT name FROM master.sys.databases WHERE HAS_DBACCESS(name) = 1 AND state = 0 ORDER BY name"
            );

            if (databases.Rows.Count == 0)
            {
                Logger.Warning("No accessible databases found.");
                return null;
            }

            Logger.Info($"Found {databases.Rows.Count} accessible database(s) to query: {string.Join(", ", databases.AsEnumerable().Select(row => row["name"].ToString()))}");

            // Store original query
            string originalQuery = _query;
            
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
                    // Modify query to switch database context
                    _query = $"USE [{dbName}]; {originalQuery}";

                    // Execute using base class - it returns the DataTable
                    object? result = base.Execute(databaseContext);

                    // Merge results if it's a DataTable
                    if (result is DataTable dbResults && dbResults.Rows.Count > 0)
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
                        Logger.SuccessNested($"Retrieved {dbResults.Rows.Count} row(s)");
                    }
                }
                catch (Exception ex)
                {
                    Logger.WarningNested($"Error: {ex.Message}");
                    continue;
                }
            }

            // Restore original query
            _query = originalQuery;

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
