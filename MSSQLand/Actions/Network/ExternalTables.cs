using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;


namespace MSSQLand.Actions.Network
{
    /// <summary>
    /// Retrieves external tables configured on the SQL Server instance.
    /// 
    /// External tables provide access to data stored outside the database:
    /// - Azure SQL Database: Elastic Query tables accessing remote databases
    /// - Azure Synapse: Tables backed by data lake files (Parquet, CSV)
    /// - SQL Server with PolyBase: Tables accessing Hadoop, Azure Blob Storage
    /// 
    /// Attack Surface:
    /// - External tables may expose sensitive data from remote sources
    /// - Table schemas reveal data structure of external systems
    /// - With SELECT permission, you can query data from external sources
    /// - Location information reveals storage accounts, databases, or file paths
    /// - Can be used to identify data exfiltration paths or misconfigurations
    /// </summary>
    internal class ExternalTables : BaseAction
    {


        public override void ValidateArguments(string additionalArguments)
        {
            // No additional arguments needed
        }


        /// <summary>
        /// Executes the external tables action to retrieve external table definitions.
        /// </summary>
        /// <param name="databaseContext">The DatabaseContext for executing the query.</param>
        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Retrieving External Tables");
            
            // Check if sys.external_tables exists
            string checkQuery = @"
                SELECT COUNT(*) 
                FROM sys.all_objects 
                WHERE object_id = OBJECT_ID('sys.external_tables') 
                AND type = 'V'";
            
            int viewExists = databaseContext.QueryService.ExecuteScalar<int>(checkQuery);
            
            if (viewExists == 0)
            {
                Logger.Warning("External tables are not available on this SQL Server instance.");
                Logger.InfoNested("This feature requires:");
                Logger.InfoNested("  - Azure SQL Database (Elastic Query)");
                Logger.InfoNested("  - Azure Synapse Analytics");
                Logger.InfoNested("  - SQL Server 2016+ with PolyBase");
                Logger.NewLine();
                return null;
            }

            DataTable resultTable = GetExternalTables(databaseContext);

            if (resultTable.Rows.Count == 0)
            {
                Logger.Warning("No external tables found in the current database.");
                Logger.InfoNested("External tables are virtual tables that reference external data sources.");
            }
            else
            {
                Console.WriteLine(OutputFormatter.ConvertDataTable(resultTable));
                Logger.NewLine();
                Logger.Info("Attack Vectors:");
                Logger.InfoNested("- Query external tables to access remote data");
                Logger.InfoNested("- Analyze schemas to understand external system structure");
                Logger.InfoNested("- Use for data exfiltration if you have CREATE EXTERNAL TABLE permission");
            }

            return resultTable;
        }


        /// <summary>
        /// Retrieves external tables from sys.external_tables.
        /// </summary>
        private static DataTable GetExternalTables(DatabaseContext databaseContext)
        {
            string query = @"
                SELECT
                    SCHEMA_NAME(et.schema_id) AS [Schema],
                    et.name AS [Table Name],
                    eds.name AS [Data Source],
                    eds.location AS [Location],
                    eff.name AS [File Format],
                    et.location AS [Remote Location],
                    CASE 
                        WHEN et.reject_type = 0 THEN 'VALUE'
                        WHEN et.reject_type = 1 THEN 'PERCENTAGE'
                        ELSE 'UNKNOWN'
                    END AS [Reject Type],
                    et.reject_value AS [Reject Value],
                    et.reject_sample_value AS [Reject Sample]
                FROM sys.external_tables et
                LEFT JOIN sys.external_data_sources eds ON et.data_source_id = eds.data_source_id
                LEFT JOIN sys.external_file_formats eff ON et.file_format_id = eff.file_format_id
                ORDER BY et.name;";

            return databaseContext.QueryService.ExecuteTable(query);
        }
    }
}
