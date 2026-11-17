// MSSQLand/Actions/Cloud/Rds/RdsS3.cs

using System;
using System.Data;

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;

namespace MSSQLand.Actions.Cloud.Rds
{
    /// <summary>
    /// List files in the RDS staging directory (D:\S3\).
    /// Uses rds_gather_file_details (async) then rds_fn_list_file_details.
    /// </summary>
    internal class RdsS3List : BaseAction
    {
        public override object Execute(DatabaseContext databaseContext)
        {
            if (!RdsDetect.RequiresRds(databaseContext))
            {
                return null;
            }

            QueryService qs = databaseContext.QueryService;
            Logger.Info(@"Listing files in RDS staging directory (D:\S3\)");

            try
            {
                qs.ExecuteNonProcessing("EXEC msdb.dbo.rds_gather_file_details;");
            }
            catch (Exception ex)
            {
                Logger.Error($"rds_gather_file_details failed: {ex.Message}");
                return null;
            }

            try
            {
                DataTable dt = qs.ExecuteTable("SELECT * FROM msdb.dbo.rds_fn_list_file_details();");
                if (dt.Rows.Count == 0)
                {
                    Logger.Warning(@"No files found in D:\S3\");
                    return null;
                }

                Logger.Success($"Found {dt.Rows.Count} file(s)");
                Console.WriteLine(OutputFormatter.ConvertDataTable(dt));
                return dt;
            }
            catch (Exception ex)
            {
                Logger.Error($"Could not list files: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Download a file from S3 to the RDS instance staging directory (D:\S3\).
    /// Requires the IAM role to have s3:GetObject.
    /// </summary>
    internal class RdsS3Download : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "s", LongName = "s3-arn", Required = true, Description = "S3 ARN, e.g. arn:aws:s3:::bucket/file.csv")]
        private string _s3Arn = "";

        [ArgumentMetadata(Position = 1, ShortName = "f", LongName = "file", Required = false, Description = @"Local filename in D:\S3\ (default: derived from ARN)")]
        private string _filename = "";

        [ArgumentMetadata(ShortName = "o", LongName = "overwrite", Required = false, Description = "Overwrite if file exists")]
        private bool _overwrite = false;

        public override object Execute(DatabaseContext databaseContext)
        {
            if (!RdsDetect.RequiresRds(databaseContext))
            {
                return null;
            }

            QueryService qs = databaseContext.QueryService;

            string filename = _filename;
            if (string.IsNullOrEmpty(filename))
            {
                // Derive filename from ARN
                int lastSlash = _s3Arn.TrimEnd('/').LastIndexOf('/');
                filename = lastSlash >= 0 ? _s3Arn.Substring(lastSlash + 1) : _s3Arn;
            }

            string rdsPath = $@"D:\S3\{filename}";
            int overwrite = _overwrite ? 1 : 0;
            string arn = _s3Arn.Replace("'", "''");
            string path = rdsPath.Replace("'", "''");

            Logger.Info($"Downloading {_s3Arn} -> {rdsPath}");

            string query = $"EXEC msdb.dbo.rds_download_from_s3 @s3_arn_of_file = '{arn}', @rds_file_path = '{path}', @overwrite_file = {overwrite};";

            try
            {
                DataTable dt = qs.ExecuteTable(query);
                if (dt.Rows.Count > 0 && dt.Columns.Contains("task_id"))
                {
                    string taskId = dt.Rows[0]["task_id"]?.ToString();
                    Logger.Success($"Download task started (task_id: {taskId})");
                    Logger.InfoNested("Monitor with: rds-tasks");
                    return taskId;
                }

                Logger.Warning("No task ID returned");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Download failed: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Upload a file from the RDS staging directory (D:\S3\) to S3.
    /// Requires the IAM role to have s3:PutObject.
    /// </summary>
    internal class RdsS3Upload : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "f", LongName = "file", Required = true, Description = @"File path on the RDS instance (D:\S3\...)")]
        private string _rdsPath = "";

        [ArgumentMetadata(Position = 1, ShortName = "s", LongName = "s3-arn", Required = true, Description = "Target S3 ARN, e.g. arn:aws:s3:::bucket/output.csv")]
        private string _s3Arn = "";

        [ArgumentMetadata(ShortName = "o", LongName = "overwrite", Required = false, Description = "Overwrite if S3 object exists")]
        private bool _overwrite = false;

        public override object Execute(DatabaseContext databaseContext)
        {
            if (!RdsDetect.RequiresRds(databaseContext))
            {
                return null;
            }

            QueryService qs = databaseContext.QueryService;

            int overwrite = _overwrite ? 1 : 0;
            string path = _rdsPath.Replace("'", "''");
            string arn = _s3Arn.Replace("'", "''");

            Logger.Info($"Uploading {_rdsPath} -> {_s3Arn}");

            string query = $"EXEC msdb.dbo.rds_upload_to_s3 @rds_file_path = '{path}', @s3_arn_of_file = '{arn}', @overwrite_file = {overwrite};";

            try
            {
                DataTable dt = qs.ExecuteTable(query);
                if (dt.Rows.Count > 0 && dt.Columns.Contains("task_id"))
                {
                    string taskId = dt.Rows[0]["task_id"]?.ToString();
                    Logger.Success($"Upload task started (task_id: {taskId})");
                    Logger.InfoNested("Monitor with: rds-tasks");
                    return taskId;
                }

                Logger.Warning("No task ID returned");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Upload failed: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Delete a file from the RDS staging directory (D:\S3\).
    /// </summary>
    internal class RdsS3Delete : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "f", LongName = "file", Required = true, Description = @"File path to delete (D:\S3\...)")]
        private string _rdsPath = "";

        public override object Execute(DatabaseContext databaseContext)
        {
            if (!RdsDetect.RequiresRds(databaseContext))
            {
                return null;
            }

            QueryService qs = databaseContext.QueryService;
            string path = _rdsPath.Replace("'", "''");

            Logger.Info($"Deleting {_rdsPath}");

            try
            {
                qs.ExecuteNonProcessing($"EXEC msdb.dbo.rds_delete_from_filesystem @rds_file_path = '{path}';");
                Logger.Success($"Deleted {_rdsPath}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Delete failed: {ex.Message}");
                return null;
            }
        }
    }
}
