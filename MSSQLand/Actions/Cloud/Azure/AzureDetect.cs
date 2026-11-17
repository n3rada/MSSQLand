// MSSQLand/Actions/Cloud/Azure/AzureDetect.cs

using System;
using System.Collections.Generic;
using System.Data;

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;

namespace MSSQLand.Actions.Cloud.Azure
{
    /// <summary>
    /// Fingerprint Azure SQL Database: engine edition, service tier, TDE status,
    /// geo-replication links, and feature availability probes.
    /// </summary>
    internal class AzureDetect : BaseAction
    {
        [ExcludeFromArguments]
        private static readonly Dictionary<int, string> EditionNames = new()
        {
            { 5, "Azure SQL Database" },
            { 6, "Azure Synapse Analytics" },
            { 8, "Azure SQL Managed Instance" },
            { 9, "Azure SQL Edge" },
            { 11, "Azure Synapse serverless SQL pool" }
        };

        public override object Execute(DatabaseContext databaseContext)
        {
            if (!databaseContext.QueryService.ExecutionServer.IsAzureSQL)
            {
                Logger.Warning("This does not appear to be Azure SQL Database");
                return null;
            }

            Logger.Success("Azure SQL Database detected");

            ServiceMetadata(databaseContext.QueryService);
            TdeStatus(databaseContext.QueryService);
            GeoReplication(databaseContext.QueryService);
            FeatureAvailability(databaseContext.QueryService);

            return true;
        }

        private static void ServiceMetadata(QueryService qs)
        {
            var queries = new Dictionary<string, string>
            {
                { "Engine Edition", "SELECT CAST(SERVERPROPERTY('EngineEdition') AS INT)" },
                { "Edition", "SELECT CAST(DATABASEPROPERTYEX(DB_NAME(), 'Edition') AS NVARCHAR(256))" },
                { "Service Objective", "SELECT CAST(DATABASEPROPERTYEX(DB_NAME(), 'ServiceObjective') AS NVARCHAR(256))" },
                { "Max Size (bytes)", "SELECT CAST(DATABASEPROPERTYEX(DB_NAME(), 'MaxSizeInBytes') AS BIGINT)" },
                { "Elastic Pool", "SELECT CAST(DATABASEPROPERTYEX(DB_NAME(), 'ElasticPoolName') AS NVARCHAR(256))" },
                { "Contained Auth", "SELECT CASE WHEN DATABASEPROPERTYEX(DB_NAME(), 'Containment') = 1 THEN 'Partial' WHEN DATABASEPROPERTYEX(DB_NAME(), 'Containment') = 0 THEN 'None' ELSE CAST(DATABASEPROPERTYEX(DB_NAME(), 'Containment') AS NVARCHAR(32)) END" }
            };

            var results = new Dictionary<string, string>();

            foreach (var entry in queries)
            {
                try
                {
                    DataTable dt = qs.ExecuteTable(entry.Key == "Engine Edition" ? entry.Value : entry.Value);
                    string value = dt.Rows.Count > 0 ? dt.Rows[0][0]?.ToString() ?? "N/A" : "N/A";

                    if (entry.Key == "Engine Edition" && int.TryParse(value, out int code))
                    {
                        string desc = EditionNames.TryGetValue(code, out string name) ? name : "Unknown";
                        value = $"{code} ({desc})";
                    }
                    else if (entry.Key == "Max Size (bytes)")
                    {
                        if (long.TryParse(value, out long bytes) && bytes > 0)
                        {
                            value = $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
                        }
                        else
                        {
                            value = "Unlimited or default";
                        }
                    }

                    results[entry.Key] = value;
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Could not retrieve {entry.Key}: {ex.Message}");
                }
            }

            if (results.Count > 0)
            {
                Logger.Info("Azure SQL service metadata");
                Console.WriteLine(OutputFormatter.ConvertDictionary(results, "Property", "Value"));
            }
        }

        private static void TdeStatus(QueryService qs)
        {
            string query = @"
                SELECT db.name AS [Database],
                    CASE tde.encryption_state
                        WHEN 0 THEN 'No encryption key'
                        WHEN 1 THEN 'Unencrypted'
                        WHEN 2 THEN 'Encryption in progress'
                        WHEN 3 THEN 'Encrypted'
                        WHEN 4 THEN 'Key change in progress'
                        WHEN 5 THEN 'Decryption in progress'
                        WHEN 6 THEN 'Protection change in progress'
                        ELSE CAST(tde.encryption_state AS NVARCHAR(10))
                    END AS [Encryption State],
                    tde.encryptor_type AS [Encryptor Type],
                    tde.key_algorithm AS [Algorithm],
                    tde.key_length AS [Key Length]
                FROM sys.dm_database_encryption_keys tde
                JOIN sys.databases db ON tde.database_id = db.database_id;";

            try
            {
                DataTable dt = qs.ExecuteTable(query);
                if (dt.Rows.Count == 0)
                {
                    return;
                }

                Logger.Info("Transparent Data Encryption status");
                Console.WriteLine(OutputFormatter.ConvertDataTable(dt));
            }
            catch
            {
                Logger.Debug("TDE status not available (no permission or not enabled)");
            }
        }

        private static void GeoReplication(QueryService qs)
        {
            string query = @"
                SELECT link_guid, partner_server, partner_database,
                    role_desc AS [Role], replication_state_desc AS [State],
                    last_replication, replication_lag_sec AS [Lag (sec)]
                FROM sys.geo_replication_links;";

            try
            {
                DataTable dt = qs.ExecuteTable(query);
                if (dt.Rows.Count == 0)
                {
                    Logger.Debug("No geo-replication links found");
                    return;
                }

                Logger.Info($"Found {dt.Rows.Count} geo-replication link(s)");
                Console.WriteLine(OutputFormatter.ConvertDataTable(dt));
            }
            catch
            {
                Logger.Debug("Geo-replication view not accessible");
            }
        }

        private static void FeatureAvailability(QueryService qs)
        {
            var checks = new (string Feature, string Query)[]
            {
                ("sp_invoke_external_rest_endpoint",
                 "SELECT CASE WHEN OBJECT_ID('sp_invoke_external_rest_endpoint') IS NOT NULL THEN 'Available' ELSE 'Not available' END"),
                ("Database-scoped credentials",
                 "SELECT CASE WHEN EXISTS(SELECT 1 FROM sys.database_scoped_credentials) THEN 'Present' ELSE 'None found' END"),
                ("External data sources",
                 "SELECT CASE WHEN EXISTS(SELECT 1 FROM sys.external_data_sources) THEN 'Present' ELSE 'None found' END"),
                ("Database-level firewall rules",
                 "SELECT CASE WHEN EXISTS(SELECT 1 FROM sys.database_firewall_rules) THEN 'Present' ELSE 'None found' END")
            };

            var results = new Dictionary<string, string>();

            foreach (var (feature, query) in checks)
            {
                try
                {
                    DataTable dt = qs.ExecuteTable(query);
                    string value = dt.Rows.Count > 0 ? dt.Rows[0][0]?.ToString() ?? "Unknown" : "Unknown";
                    results[feature] = value;
                }
                catch
                {
                    results[feature] = "Check failed";
                }
            }

            if (results.Count > 0)
            {
                Logger.Info("Feature availability");
                Console.WriteLine(OutputFormatter.ConvertDictionary(results, "Feature", "Status"));
            }
        }
    }
}
