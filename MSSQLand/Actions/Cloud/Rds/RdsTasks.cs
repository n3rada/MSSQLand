// MSSQLand/Actions/Cloud/Rds/RdsTasks.cs

using System;
using System.Collections.Generic;
using System.Data;

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;

namespace MSSQLand.Actions.Cloud.Rds
{
    /// <summary>
    /// Query rds_fn_task_status to enumerate past and running RDS tasks.
    /// History is kept for 36 days. The S3_object_arn and KMS_master_key_arn
    /// columns reveal bucket names, paths, and encryption keys.
    /// </summary>
    internal class RdsTasks : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "d", LongName = "db", Required = false, Description = "Filter tasks by database name")]
        private string _database = "";

        public override object Execute(DatabaseContext databaseContext)
        {
            if (!RdsDetect.RequiresRds(databaseContext))
            {
                return null;
            }

            QueryService qs = databaseContext.QueryService;
            string query;

            if (!string.IsNullOrEmpty(_database))
            {
                Logger.Info($"Querying RDS task history for database '{_database}'");
                string db = _database.Replace("'", "''");
                query = $"EXEC msdb.dbo.rds_task_status @db_name = '{db}';";
            }
            else
            {
                Logger.Info("Querying full RDS task history");
                query = "SELECT * FROM msdb.dbo.rds_fn_task_status(NULL, 0);";
            }

            try
            {
                DataTable dt = qs.ExecuteTable(query);
                if (dt.Rows.Count == 0)
                {
                    Logger.Warning("No RDS tasks found (history kept for 36 days)");
                    return null;
                }

                // Extract S3 ARNs and KMS keys from results
                var s3Arns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var kmsKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (dt.Columns.Contains("S3_object_arn"))
                {
                    foreach (DataRow row in dt.Rows)
                    {
                        string arn = row["S3_object_arn"]?.ToString();
                        if (!string.IsNullOrEmpty(arn))
                        {
                            s3Arns.Add(arn);
                        }
                    }
                }

                if (dt.Columns.Contains("KMS_master_key_arn"))
                {
                    foreach (DataRow row in dt.Rows)
                    {
                        string key = row["KMS_master_key_arn"]?.ToString();
                        if (!string.IsNullOrEmpty(key))
                        {
                            kmsKeys.Add(key);
                        }
                    }
                }

                Logger.Success($"Found {dt.Rows.Count} task(s)");
                if (s3Arns.Count > 0)
                {
                    Logger.InfoNested($"S3 buckets seen: {string.Join(", ", s3Arns)}");
                }
                if (kmsKeys.Count > 0)
                {
                    Logger.InfoNested($"KMS keys seen: {string.Join(", ", kmsKeys)}");
                }

                Console.WriteLine(OutputFormatter.ConvertDataTable(dt));
                return dt;
            }
            catch (Exception ex)
            {
                string message = ex.Message.ToLower();
                if (message.Contains("rdsadmin") || message.Contains("does not exist"))
                {
                    Logger.Error("Not an RDS instance or no permission on rds_fn_task_status");
                }
                else
                {
                    Logger.Error($"Failed to query RDS task history: {ex.Message}");
                }
                return null;
            }
        }
    }
}
