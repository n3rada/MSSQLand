// MSSQLand/Actions/Cloud/Rds/RdsDetect.cs

using System;
using System.Data;

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;

namespace MSSQLand.Actions.Cloud.Rds
{
    /// <summary>
    /// Detect Amazon RDS for SQL Server: enumerate rds_* procedures in msdb,
    /// check EXECUTE permissions on each, and query Multi-AZ failover history.
    /// </summary>
    internal class RdsDetect : BaseAction
    {
        public override object Execute(DatabaseContext databaseContext)
        {
            QueryService qs = databaseContext.QueryService;
            bool cached = qs.ExecutionServer.IsRds.HasValue;

            if (!qs.IsRds())
            {
                string hint = cached ? " (cached)" : "";
                Logger.Warning($"This does not appear to be Amazon RDS{hint}");
                return null;
            }

            Logger.Success("Amazon RDS for SQL Server detected");

            EnumerateProcedures(qs);
            FailoverTime(qs);

            return true;
        }

        private static void EnumerateProcedures(QueryService qs)
        {
            string query = @"
                SELECT
                    p.name AS [Procedure],
                    CASE
                        WHEN HAS_PERMS_BY_NAME('msdb.dbo.' + p.name, 'OBJECT', 'EXECUTE') = 1
                        THEN 'Yes' ELSE 'No'
                    END AS [Can Execute]
                FROM msdb.sys.procedures p
                WHERE p.name LIKE 'rds_%'
                UNION ALL
                SELECT
                    f.name AS [Procedure],
                    CASE
                        WHEN HAS_PERMS_BY_NAME('msdb.dbo.' + f.name, 'OBJECT', 'EXECUTE') = 1
                        THEN 'Yes' ELSE 'No'
                    END AS [Can Execute]
                FROM msdb.sys.objects f
                WHERE f.name LIKE 'rds_fn_%' AND f.type IN ('FN','IF','TF')
                ORDER BY [Procedure];";

            try
            {
                DataTable dt = qs.ExecuteTable(query);
                if (dt.Rows.Count == 0)
                {
                    Logger.Warning("No rds_* procedures found in msdb");
                    return;
                }

                int executable = 0;
                foreach (DataRow row in dt.Rows)
                {
                    if ("Yes".Equals(row["Can Execute"]?.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        executable++;
                    }
                }

                Logger.Info($"Found {dt.Rows.Count} RDS procedure(s), {executable} executable");
                Console.WriteLine(OutputFormatter.ConvertDataTable(dt));
            }
            catch (Exception ex)
            {
                Logger.Warning($"Could not enumerate RDS procedures: {ex.Message}");
            }
        }

        private static void FailoverTime(QueryService qs)
        {
            try
            {
                DataTable dt = qs.ExecuteTable("EXEC msdb.dbo.rds_failover_time;");
                if (dt.Rows.Count == 0)
                {
                    return;
                }

                Logger.Info("Multi-AZ failover history");
                Console.WriteLine(OutputFormatter.ConvertDataTable(dt));
            }
            catch
            {
                Logger.Debug("rds_failover_time not available (single-AZ or no permission)");
            }
        }

        /// <summary>
        /// Guard: checks IsRds and logs a clear message if not.
        /// </summary>
        internal static bool RequiresRds(DatabaseContext databaseContext)
        {
            bool cached = databaseContext.QueryService.ExecutionServer.IsRds.HasValue;
            if (databaseContext.QueryService.IsRds())
            {
                return true;
            }

            if (cached)
            {
                Logger.Error("This action requires Amazon RDS for SQL Server (cached)");
            }
            else
            {
                Logger.Error("This action requires Amazon RDS for SQL Server");
            }
            return false;
        }
    }
}
