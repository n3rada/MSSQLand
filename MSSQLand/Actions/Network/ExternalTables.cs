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
            // Get external tables
            string tablesQuery = "SELECT * FROM sys.external_tables ORDER BY name;";
            DataTable rawTables = databaseContext.QueryService.ExecuteTable(tablesQuery);

            if (rawTables == null || rawTables.Rows.Count == 0)
            {
                return new DataTable();
            }

            // Get external data sources for lookup
            string dataSourcesQuery = "SELECT data_source_id, name, location FROM sys.external_data_sources;";
            DataTable dataSources = null;
            var dataSourcesDict = new System.Collections.Generic.Dictionary<int, (string name, string location)>();
            
            try
            {
                dataSources = databaseContext.QueryService.ExecuteTable(dataSourcesQuery);
                if (dataSources != null)
                {
                    foreach (DataRow row in dataSources.Rows)
                    {
                        int id = Convert.ToInt32(row["data_source_id"]);
                        string name = row["name"]?.ToString() ?? "";
                        string location = row["location"]?.ToString() ?? "";
                        dataSourcesDict[id] = (name, location);
                    }
                }
            }
            catch { }

            // Get external file formats for lookup
            string fileFormatsQuery = "SELECT file_format_id, name FROM sys.external_file_formats;";
            DataTable fileFormats = null;
            var fileFormatsDict = new System.Collections.Generic.Dictionary<int, string>();
            
            try
            {
                fileFormats = databaseContext.QueryService.ExecuteTable(fileFormatsQuery);
                if (fileFormats != null)
                {
                    foreach (DataRow row in fileFormats.Rows)
                    {
                        int id = Convert.ToInt32(row["file_format_id"]);
                        string name = row["name"]?.ToString() ?? "";
                        fileFormatsDict[id] = name;
                    }
                }
            }
            catch { }

            // Create formatted output table
            DataTable result = new DataTable();
            result.Columns.Add("Schema", typeof(string));
            result.Columns.Add("Table Name", typeof(string));
            result.Columns.Add("Data Source", typeof(string));
            result.Columns.Add("Data Source Location", typeof(string));
            result.Columns.Add("File Format", typeof(string));
            result.Columns.Add("Table Location", typeof(string));
            result.Columns.Add("Reject Type", typeof(string));
            result.Columns.Add("Reject Value", typeof(string));
            result.Columns.Add("Distribution", typeof(string));

            foreach (DataRow row in rawTables.Rows)
            {
                DataRow newRow = result.NewRow();

                // Schema and table name
                int schemaId = rawTables.Columns.Contains("schema_id") && row["schema_id"] != DBNull.Value 
                    ? Convert.ToInt32(row["schema_id"]) 
                    : 0;
                
                if (schemaId > 0)
                {
                    try
                    {
                        string schemaQuery = $"SELECT SCHEMA_NAME({schemaId});";
                        var schemaResult = databaseContext.QueryService.ExecuteScalar(schemaQuery);
                        newRow["Schema"] = schemaResult?.ToString() ?? "dbo";
                    }
                    catch
                    {
                        newRow["Schema"] = "dbo";
                    }
                }
                else
                {
                    newRow["Schema"] = "dbo";
                }

                newRow["Table Name"] = row["name"]?.ToString() ?? "";

                // Data source lookup
                if (rawTables.Columns.Contains("data_source_id") && row["data_source_id"] != DBNull.Value)
                {
                    int dsId = Convert.ToInt32(row["data_source_id"]);
                    if (dataSourcesDict.TryGetValue(dsId, out var ds))
                    {
                        newRow["Data Source"] = ds.name;
                        newRow["Data Source Location"] = ds.location;
                    }
                    else
                    {
                        newRow["Data Source"] = $"ID: {dsId}";
                        newRow["Data Source Location"] = "";
                    }
                }
                else
                {
                    newRow["Data Source"] = "";
                    newRow["Data Source Location"] = "";
                }

                // File format lookup
                if (rawTables.Columns.Contains("file_format_id") && row["file_format_id"] != DBNull.Value)
                {
                    int ffId = Convert.ToInt32(row["file_format_id"]);
                    if (fileFormatsDict.TryGetValue(ffId, out string ffName))
                    {
                        newRow["File Format"] = ffName;
                    }
                    else
                    {
                        newRow["File Format"] = $"ID: {ffId}";
                    }
                }
                else
                {
                    newRow["File Format"] = "";
                }

                // Table location (HDFS path, file path, etc.)
                newRow["Table Location"] = rawTables.Columns.Contains("location") 
                    ? (row["location"]?.ToString() ?? "") 
                    : "";

                // Reject type and value (for HADOOP external data sources)
                if (rawTables.Columns.Contains("reject_type") && row["reject_type"] != DBNull.Value)
                {
                    byte rejectType = Convert.ToByte(row["reject_type"]);
                    string rejectTypeStr = rejectType == 0 ? "VALUE" : rejectType == 1 ? "PERCENTAGE" : "UNKNOWN";
                    
                    string rejectValue = "";
                    if (rawTables.Columns.Contains("reject_value") && row["reject_value"] != DBNull.Value)
                    {
                        rejectValue = row["reject_value"].ToString();
                    }
                    
                    if (rawTables.Columns.Contains("reject_sample_value") && row["reject_sample_value"] != DBNull.Value && rejectType == 1)
                    {
                        rejectValue += $" (sample: {row["reject_sample_value"]})";
                    }
                    
                    newRow["Reject Type"] = rejectTypeStr;
                    newRow["Reject Value"] = rejectValue;
                }
                else
                {
                    newRow["Reject Type"] = "";
                    newRow["Reject Value"] = "";
                }

                // Distribution (for SHARD_MAP_MANAGER external data sources)
                if (rawTables.Columns.Contains("distribution_desc"))
                {
                    newRow["Distribution"] = row["distribution_desc"]?.ToString() ?? "";
                }
                else
                {
                    newRow["Distribution"] = "";
                }

                result.Rows.Add(newRow);
            }

            return result;
        }
    }
}
