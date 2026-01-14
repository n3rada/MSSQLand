using MSSQLand.Services;
using MSSQLand.Utilities.Formatters;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Security.Principal;

namespace MSSQLand.Utilities
{
    /// <summary>
    /// Standalone utility for enumerating SQL Servers in an Active Directory domain via LDAP queries.
    /// Does not require database authentication or connection.
    /// </summary>
    public static class FindSQLServers
    {
        /// <summary>
        /// Enumerates SQL Servers in the specified Active Directory domain.
        /// </summary>
        /// <param name="domain">The Active Directory domain to query</param>
        /// <param name="forest">If true, queries the Global Catalog for forest-wide results</param>
        /// <returns>Number of SQL Servers found</returns>
        public static int Execute(string domain, bool forest = false)
        {
            if (string.IsNullOrWhiteSpace(domain))
            {
                throw new ArgumentException("Active Directory domain is required. Please provide a valid domain name.");
            }

            string protocol = forest ? "GC" : "LDAP";
            string scope = forest ? "forest-wide (Global Catalog)" : "domain";
            
            Logger.Task($"Lurking for MS SQL Servers on Active Directory {scope}: {domain}");
            Logger.TaskNested("This method discovers servers with Kerberos SPNs registered in AD.");
            
            if (forest)
            {
                Logger.TaskNested("Using Global Catalog (port 3268) to query all domains in the forest.");
            }

            // Initialize domain service based on the provided domain
            ADirectoryService domainService = new($"{protocol}://{domain}");
            LdapQueryService ldapService = new(domainService);

            // LDAP filter and properties for MS SQL SPNs
            // SQL Server SPNs use the service class "MSSQLSvc"
            const string ldapFilter = "(servicePrincipalName=MSSQL*)";
            string[] ldapAttributes = { "cn", "samaccountname", "objectsid", "serviceprincipalname", "lastlogon" };

            // Execute the LDAP query
            Dictionary<string, Dictionary<string, object[]>> ldapResults = ldapService.ExecuteQuery(ldapFilter, ldapAttributes);

            // Track unique servers with their instances
            var serverMap = new Dictionary<string, ServerInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (Dictionary<string, object[]> ldapEntry in ldapResults.Values)
            {
                // Check if serviceprincipalname exists
                if (!ldapEntry.TryGetValue("serviceprincipalname", out object[] spnValues) || spnValues == null || spnValues.Length == 0)
                {
                    continue;
                }

                // Extract LDAP properties with null checks
                string accountName = ldapEntry.TryGetValue("samaccountname", out object[] samValues) && samValues?.Length > 0
                    ? samValues[0]?.ToString() ?? "(unknown)"
                    : "(unknown)";

                string objectSid = "(unknown)";
                if (ldapEntry.TryGetValue("objectsid", out object[] sidValues) && sidValues?.Length > 0 && sidValues[0] is byte[] objectSidBytes)
                {
                    try
                    {
                        objectSid = new SecurityIdentifier(objectSidBytes, 0).ToString();
                    }
                    catch
                    {
                        objectSid = "(invalid SID)";
                    }
                }

                string lastLogonDate = "(never)";
                if (ldapEntry.TryGetValue("lastlogon", out object[] logonValues) && logonValues?.Length > 0)
                {
                    try
                    {
                        long lastLogonTimestamp = Convert.ToInt64(logonValues[0]);
                        if (lastLogonTimestamp > 0)
                        {
                            lastLogonDate = DateTime.FromFileTime(lastLogonTimestamp).ToString("yyyy-MM-dd HH:mm");
                        }
                    }
                    catch
                    {
                        lastLogonDate = "(invalid)";
                    }
                }

                foreach (string spn in spnValues.Cast<string>())
                {
                    // Skip non-SQL SPNs
                    if (!spn.StartsWith("MSSQLSvc", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    int serviceDelimiterIndex = spn.IndexOf('/');
                    if (serviceDelimiterIndex < 0)
                    {
                        continue;
                    }

                    string serviceInstance = spn.Substring(serviceDelimiterIndex + 1);

                    int portDelimiterIndex = serviceInstance.IndexOf(':');
                    string serverName = portDelimiterIndex == -1
                        ? serviceInstance
                        : serviceInstance.Substring(0, portDelimiterIndex);

                    string instanceOrPort = portDelimiterIndex == -1
                        ? "default"
                        : serviceInstance.Substring(portDelimiterIndex + 1);

                    // Add or update server entry
                    if (!serverMap.TryGetValue(serverName, out ServerInfo serverInfo))
                    {
                        // Resolve the server's IP address
                        string serverIpAddress;
                        try
                        {
                            IPAddress[] ipAddresses = Dns.GetHostAddresses(serverName);
                            serverIpAddress = ipAddresses.Length > 0 ? ipAddresses[0].ToString() : "-";
                        }
                        catch (Exception)
                        {
                            serverIpAddress = "-";
                        }

                        serverInfo = new ServerInfo
                        {
                            ServerName = serverName,
                            IpAddress = serverIpAddress,
                            AccountName = accountName,
                            ObjectSid = objectSid,
                            LastLogon = lastLogonDate,
                            Instances = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        };
                        serverMap[serverName] = serverInfo;
                    }

                    serverInfo.Instances.Add(instanceOrPort);
                }
            }

            if (serverMap.Count == 0)
            {
                Logger.Warning("No SQL Servers found.");
                return 0;
            }

            // Build output table
            DataTable resultTable = new();
            resultTable.Columns.Add("Server", typeof(string));
            resultTable.Columns.Add("DNS Resolution", typeof(string));
            resultTable.Columns.Add("Instances", typeof(string));
            resultTable.Columns.Add("Service Account", typeof(string));
            resultTable.Columns.Add("Account SID", typeof(string));
            resultTable.Columns.Add("Last Logon", typeof(string));

            foreach (var server in serverMap.Values.OrderBy(s => s.ServerName))
            {
                resultTable.Rows.Add(
                    server.ServerName,
                    server.IpAddress,
                    string.Join(", ", server.Instances.OrderBy(i => i)),
                    server.AccountName,
                    server.ObjectSid,
                    server.LastLogon
                );
            }

            Console.WriteLine(OutputFormatter.ConvertDataTable(resultTable));

            int totalInstances = serverMap.Values.Sum(s => s.Instances.Count);
            Logger.Success($"{serverMap.Count} unique SQL Server(s) found with {totalInstances} instance(s).");
            return serverMap.Count;
        }

        private class ServerInfo
        {
            public string ServerName { get; set; }
            public string IpAddress { get; set; }
            public string AccountName { get; set; }
            public string ObjectSid { get; set; }
            public string LastLogon { get; set; }
            public HashSet<string> Instances { get; set; }
        }
    }
}
