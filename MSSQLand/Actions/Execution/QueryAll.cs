// MSSQLand/Actions/Execution/QueryAll.cs

using System;
using System.Data;
using System.Linq;

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;

namespace MSSQLand.Actions.Execution
{
    /// <summary>
    /// Executes a T-SQL query across every accessible database on the execution server.
    /// Tries sp_MSforeachdb first; falls back to a manual USE loop if that fails.
    /// Results are combined into a single table with a leading Database column.
    /// </summary>
    public class QueryAll : Query
    {
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.Info($"Executing across all accessible databases on {databaseContext.QueryService.ExecutionServer.Hostname}: {_query}");

            Logger.NewLine();

            try
            {
                Logger.InfoNested("Attempting execution via sp_MSforeachdb");
                return ExecuteWithMSForeachDb(databaseContext);
            }
            catch (Exception ex)
            {
                Logger.Warning($"sp_MSforeachdb failed: {ex.Message}");
                Logger.InfoNested("Falling back to manual loop across databases");
                return ExecuteWithManualLoop(databaseContext);
            }
        }

        private object ExecuteWithMSForeachDb(DatabaseContext databaseContext)
        {
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

            if (resultTable == null || resultTable.Rows.Count == 0)
            {
                Logger.Info("No rows returned from any database.");
                return resultTable;
            }

            Console.WriteLine(OutputFormatter.ConvertDataTable(resultTable));
            Logger.Success($"Total rows returned: {resultTable.Rows.Count}");

            return resultTable;
        }

        private object ExecuteWithManualLoop(DatabaseContext databaseContext)
        {
            DataTable databases = databaseContext.QueryService.ExecuteTable(
                "SELECT name FROM master.sys.databases WHERE HAS_DBACCESS(name) = 1 AND state = 0 ORDER BY name"
            );

            if (databases.Rows.Count == 0)
            {
                Logger.Warning("No accessible databases found.");
                return null;
            }

            Logger.Info($"Found {databases.Rows.Count} accessible database(s): {string.Join(", ", databases.AsEnumerable().Select(row => row["name"].ToString()))}");

            DataTable combinedResults = null;
            int totalRows = 0;

            foreach (DataRow dbRow in databases.Rows)
            {
                string dbName = dbRow["name"].ToString();
                Logger.InfoNested($"Querying: {dbName}");

                try
                {
                    DataTable dbResults = ExecuteOn(databaseContext, $"USE [{dbName}]; {_query}");

                    if (dbResults != null && dbResults.Rows.Count > 0)
                    {
                        if (combinedResults == null)
                        {
                            combinedResults = dbResults.Clone();
                            combinedResults.Columns.Add("Database", typeof(string));
                            combinedResults.Columns["Database"].SetOrdinal(0);
                        }

                        foreach (DataRow row in dbResults.Rows)
                        {
                            DataRow newRow = combinedResults.NewRow();
                            for (int i = 0; i < dbResults.Columns.Count; i++)
                                newRow[i + 1] = row[i];
                            newRow["Database"] = dbName;
                            combinedResults.Rows.Add(newRow);
                        }

                        totalRows += dbResults.Rows.Count;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Skipping {dbName}: {ex.Message}");
                }
            }

            Logger.NewLine();

            if (combinedResults == null || combinedResults.Rows.Count == 0)
            {
                Logger.Info("No rows returned from any database.");
                return combinedResults;
            }

            Console.WriteLine(OutputFormatter.ConvertDataTable(combinedResults));
            Logger.Success($"Total rows from all databases: {totalRows}");

            return combinedResults;
        }
    }
}
