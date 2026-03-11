// MSSQLand/Actions/Agent/JobProxies.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.Agent
{
    /// <summary>
    /// Enumerate SQL Server Agent proxy accounts, their mapped logins, credentials, and allowed subsystems.
    /// Queries msdb.dbo.sysproxies, sysproxylogin, sysproxysubsystem, and sys.credentials.
    /// Proxy accounts allow job steps to run under alternate Windows credentials — valuable for credential discovery.
    /// </summary>
    internal class JobProxies : BaseAction
    {
        [ArgumentMetadata(ShortName = "l", LongName = "limit", Description = "Limit number of results (default: 25)")]
        private int _limit = 25;

        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Enumerating SQL Server Agent proxy accounts");

            // ── Proxy accounts with credential identity ──
            string topClause = BuildTopClause(_limit);

            string proxyQuery = $@"
                SELECT {topClause}
                    p.proxy_id AS ProxyId,
                    p.name AS ProxyName,
                    p.enabled AS Enabled,
                    c.name AS CredentialName,
                    c.credential_identity AS CredentialIdentity,
                    p.description AS Description
                FROM msdb.dbo.sysproxies p
                LEFT JOIN sys.credentials c
                    ON p.credential_id = c.credential_id
                ORDER BY p.name;";

            DataTable proxies = databaseContext.QueryService.ExecuteTable(proxyQuery);

            if (proxies.Rows.Count == 0)
            {
                Logger.Info("No proxy accounts found.");
                return null;
            }

            Console.WriteLine(OutputFormatter.ConvertDataTable(proxies));
            Logger.Success($"Found {proxies.Rows.Count} proxy account(s)");

            // ── Proxy → Subsystem mappings ──
            Logger.NewLine();
            Logger.Info("Proxy Subsystem Mappings");

            string subsystemQuery = @"
                SELECT
                    p.name AS ProxyName,
                    sub.subsystem AS Subsystem,
                    sub.description_id AS SubsystemDescription,
                    sub.agent_exe AS AgentExe,
                    sub.start_entry_point AS EntryPoint
                FROM msdb.dbo.sysproxysubsystem ps
                JOIN msdb.dbo.sysproxies p
                    ON ps.proxy_id = p.proxy_id
                JOIN msdb.dbo.syssubsystems sub
                    ON ps.subsystem_id = sub.subsystem_id
                ORDER BY p.name, sub.subsystem;";

            DataTable subsystems = databaseContext.QueryService.ExecuteTable(subsystemQuery);

            if (subsystems.Rows.Count == 0)
            {
                Logger.InfoNested("No subsystem mappings found");
            }
            else
            {
                Console.WriteLine(OutputFormatter.ConvertDataTable(subsystems));
                Logger.Success($"Found {subsystems.Rows.Count} subsystem mapping(s)");
            }

            // ── Proxy → Login mappings ──
            Logger.NewLine();
            Logger.Info("Proxy Login Mappings");

            string loginQuery = @"
                SELECT
                    p.name AS ProxyName,
                    SUSER_SNAME(pl.sid) AS LoginName,
                    CASE pl.flags
                        WHEN 0 THEN 'SQL Login / Windows User'
                        WHEN 1 THEN 'Server Role'
                        WHEN 2 THEN 'msdb Role'
                        ELSE CAST(pl.flags AS VARCHAR)
                    END AS LoginType
                FROM msdb.dbo.sysproxylogin pl
                JOIN msdb.dbo.sysproxies p
                    ON pl.proxy_id = p.proxy_id
                ORDER BY p.name;";

            DataTable logins = databaseContext.QueryService.ExecuteTable(loginQuery);

            if (logins.Rows.Count == 0)
            {
                Logger.InfoNested("No login mappings found");
            }
            else
            {
                Console.WriteLine(OutputFormatter.ConvertDataTable(logins));
                Logger.Success($"Found {logins.Rows.Count} login mapping(s)");
            }

            return proxies;
        }
    }
}
