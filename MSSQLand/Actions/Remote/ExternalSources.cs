using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;


namespace MSSQLand.Actions.Network
{
    /// <summary>
    /// Retrieves External Data Sources configured on the SQL Server instance.
    /// 
    /// External Data Sources enable querying external data without importing it:
    /// - Azure SQL Database: Elastic Query for cross-database queries
    /// - Azure Synapse Analytics: Query data lakes (Parquet, CSV files in Azure Data Lake Storage)
    /// - SQL Server with PolyBase: Access Hadoop, Azure Blob Storage, or other external sources
    /// 
    /// Unlike linked servers (for server-to-server connections), External Data Sources
    /// are designed for cloud storage integration and distributed data architectures.
    /// </summary>
    internal class ExternalSources : BaseAction
    {


        public override void ValidateArguments(string[] args)
        {
            // No additional arguments needed
        }


        /// <summary>
        /// Executes the external sources action to retrieve External Data Sources.
        /// Works on Azure SQL Database, Azure Synapse, and SQL Server with PolyBase.
        /// </summary>
        /// <param name="databaseContext">The DatabaseContext for executing the query.</param>
        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Retrieving External Data Sources");

            DataTable resultTable = GetExternalDataSources(databaseContext);

            if (resultTable.Rows.Count == 0)
            {
                Logger.Warning("No external data sources found in the current database.");
            }
            else
            {
                Console.WriteLine(OutputFormatter.ConvertDataTable(resultTable));
                Logger.Success($"Retrieved {resultTable.Rows.Count} external data source(s)");
            }

            return resultTable;
        }


        /// <summary>
        /// Retrieves external data sources from sys.external_data_sources.
        /// </summary>
        private static DataTable GetExternalDataSources(DatabaseContext databaseContext)
        {
            // Select all columns - different SQL Server versions have different column sets
            string query = "SELECT * FROM sys.external_data_sources ORDER BY name;";

            DataTable rawTable = databaseContext.QueryService.ExecuteTable(query);

            if (rawTable == null || rawTable.Rows.Count == 0)
            {
                return rawTable;
            }

            // Create formatted output table with common columns
            DataTable result = new DataTable();
            result.Columns.Add("Name", typeof(string));
            result.Columns.Add("Type", typeof(string));
            result.Columns.Add("Location", typeof(string));
            result.Columns.Add("Database Name", typeof(string));
            result.Columns.Add("Credential", typeof(string));
            result.Columns.Add("Connection Options", typeof(string));
            result.Columns.Add("Pushdown", typeof(string));

            foreach (DataRow row in rawTable.Rows)
            {
                DataRow newRow = result.NewRow();

                // Essential columns (always present)
                newRow["Name"] = row["name"]?.ToString() ?? "";
                newRow["Type"] = row["type_desc"]?.ToString() ?? "";
                newRow["Location"] = row["location"]?.ToString() ?? "";

                // Database name (RDBMS, SHARD_MAP_MANAGER)
                newRow["Database Name"] = rawTable.Columns.Contains("database_name") 
                    ? (row["database_name"]?.ToString() ?? "") 
                    : "";

                // Credential - handle both credential_id (actual column) and credential_name
                if (rawTable.Columns.Contains("credential_id") && row["credential_id"] != DBNull.Value)
                {
                    int credId = Convert.ToInt32(row["credential_id"]);
                    newRow["Credential"] = credId > 0 ? $"ID: {credId}" : "";
                }
                else
                {
                    newRow["Credential"] = "";
                }

                // Connection options (SQL Server 2019+)
                newRow["Connection Options"] = rawTable.Columns.Contains("connection_options") 
                    ? (row["connection_options"]?.ToString() ?? "") 
                    : "";

                // Pushdown (SQL Server 2019+) - column name is "pushdown" not "pushdown_enabled"
                newRow["Pushdown"] = rawTable.Columns.Contains("pushdown") 
                    ? (row["pushdown"]?.ToString() ?? "") 
                    : "";

                result.Rows.Add(newRow);
            }

            return result;
        }
    }
}
