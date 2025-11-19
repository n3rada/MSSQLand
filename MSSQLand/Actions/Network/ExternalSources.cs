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


        public override void ValidateArguments(string additionalArguments)
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
            
            // Check if sys.external_data_sources exists
            string checkQuery = @"
                SELECT COUNT(*) 
                FROM sys.all_objects 
                WHERE object_id = OBJECT_ID('sys.external_data_sources') 
                AND type = 'V'";
            
            int viewExists = databaseContext.QueryService.ExecuteScalar<int>(checkQuery);
            
            if (viewExists == 0)
            {
                Logger.Warning("External Data Sources are not available on this SQL Server instance.");
                Logger.InfoNested("This feature requires:");
                Logger.InfoNested("  - Azure SQL Database");
                Logger.InfoNested("  - Azure SQL Managed Instance");
                Logger.InfoNested("  - Azure Synapse Analytics");
                Logger.InfoNested("  - SQL Server with PolyBase installed");
                Logger.NewLine();
                return null;
            }

            DataTable resultTable = GetExternalDataSources(databaseContext);

            if (resultTable.Rows.Count == 0)
            {
                Logger.Warning("No external data sources found in the current database.");
                Logger.InfoNested("External Data Sources are used for:");
                Logger.InfoNested("  - Elastic Query (Azure SQL Database)");
                Logger.InfoNested("  - Cross-database queries");
                Logger.InfoNested("  - Querying Azure Blob Storage, Data Lake, Hadoop");
                Logger.InfoNested("  - PolyBase external tables");
            }
            else
            {
                Console.WriteLine(OutputFormatter.ConvertDataTable(resultTable));
            }

            return resultTable;
        }


        /// <summary>
        /// Retrieves external data sources from sys.external_data_sources.
        /// </summary>
        private static DataTable GetExternalDataSources(DatabaseContext databaseContext)
        {
            string query = @"
                SELECT
                    eds.name AS [Name],
                    eds.type_desc AS [Type],
                    eds.location AS [Location],
                    eds.database_name AS [Database Name],
                    eds.credential_name AS [Credential],
                    eds.connection_options AS [Connection Options],
                    eds.pushdown_enabled AS [Pushdown Enabled]
                FROM sys.external_data_sources eds
                ORDER BY eds.name;";

            return databaseContext.QueryService.ExecuteTable(query);
        }
    }
}
