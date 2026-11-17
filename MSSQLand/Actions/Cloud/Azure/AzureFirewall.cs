// MSSQLand/Actions/Cloud/Azure/AzureFirewall.cs

using System;
using System.Data;

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;

namespace MSSQLand.Actions.Cloud.Azure
{
    /// <summary>
    /// Enumerate Azure SQL server-level and database-level firewall rules.
    /// Flags dangerous rules: 0.0.0.0-0.0.0.0 (Allow Azure services) and
    /// 0.0.0.0-255.255.255.255 (open to the internet).
    /// </summary>
    internal class AzureFirewall : BaseAction
    {
        public override object Execute(DatabaseContext databaseContext)
        {
            if (!RequiresAzureSQL(databaseContext))
            {
                return null;
            }

            QueryService qs = databaseContext.QueryService;
            bool anyFound = false;

            anyFound |= ServerFirewallRules(qs);
            anyFound |= DatabaseFirewallRules(qs);

            if (!anyFound)
            {
                Logger.Warning("No firewall rules found (or no permission to view them)");
            }

            return anyFound ? (object)true : null;
        }

        private static bool ServerFirewallRules(QueryService qs)
        {
            string query = @"
                SELECT name AS [Rule Name],
                    start_ip_address AS [Start IP],
                    end_ip_address AS [End IP],
                    CASE
                        WHEN start_ip_address = '0.0.0.0' AND end_ip_address = '0.0.0.0'
                            THEN 'ALLOWS AZURE SERVICES'
                        WHEN start_ip_address = '0.0.0.0' AND end_ip_address = '255.255.255.255'
                            THEN 'OPEN TO INTERNET'
                        ELSE ''
                    END AS [Note],
                    create_date AS [Created],
                    modify_date AS [Modified]
                FROM sys.firewall_rules
                ORDER BY start_ip_address;";

            try
            {
                DataTable dt = qs.ExecuteTable(query);
                if (dt.Rows.Count == 0)
                {
                    Logger.Debug("No server-level firewall rules found");
                    return false;
                }

                Logger.Info($"Server-level firewall rules ({dt.Rows.Count})");
                Console.WriteLine(OutputFormatter.ConvertDataTable(dt));
                return true;
            }
            catch (Exception ex)
            {
                string message = ex.Message.ToLower();
                if (message.Contains("invalid object") || message.Contains("does not exist"))
                {
                    Logger.Debug("sys.firewall_rules not accessible (only available in master database)");
                }
                else
                {
                    Logger.Warning($"Could not query server firewall rules: {ex.Message}");
                }
                return false;
            }
        }

        private static bool DatabaseFirewallRules(QueryService qs)
        {
            string query = @"
                SELECT name AS [Rule Name],
                    start_ip_address AS [Start IP],
                    end_ip_address AS [End IP],
                    CASE
                        WHEN start_ip_address = '0.0.0.0' AND end_ip_address = '255.255.255.255'
                            THEN 'OPEN TO INTERNET'
                        ELSE ''
                    END AS [Note],
                    create_date AS [Created],
                    modify_date AS [Modified]
                FROM sys.database_firewall_rules
                ORDER BY start_ip_address;";

            try
            {
                DataTable dt = qs.ExecuteTable(query);
                if (dt.Rows.Count == 0)
                {
                    Logger.Debug("No database-level firewall rules found");
                    return false;
                }

                Logger.Info($"Database-level firewall rules ({dt.Rows.Count})");
                Console.WriteLine(OutputFormatter.ConvertDataTable(dt));
                return true;
            }
            catch (Exception ex)
            {
                string message = ex.Message.ToLower();
                if (message.Contains("invalid object") || message.Contains("does not exist"))
                {
                    Logger.Debug("sys.database_firewall_rules not accessible");
                }
                else
                {
                    Logger.Warning($"Could not query database firewall rules: {ex.Message}");
                }
                return false;
            }
        }

        /// <summary>
        /// Guard: checks the cached IsAzureSQL property and logs a clear message if not Azure.
        /// Detection is performed once at connection time via the FullVersionString setter.
        /// </summary>
        internal static bool RequiresAzureSQL(DatabaseContext databaseContext)
        {
            if (databaseContext.QueryService.ExecutionServer.IsAzureSQL)
            {
                return true;
            }

            Logger.Error("This action requires Azure SQL Database");
            return false;
        }
    }
}
