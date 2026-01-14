using MSSQLand.Services;
using MSSQLand.Utilities.Formatters;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Security.Principal;

namespace MSSQLand.Utilities.Discovery
{
    /// <summary>
    /// Standalone utility for enumerating SQL Servers in an Active Directory domain via LDAP queries.
    /// Does not require database authentication or connection.
    /// 
    /// <para>
    /// <b>How it works:</b>
    /// Queries Active Directory using two methods:
    /// 1. Objects with Kerberos SPNs starting with "MSSQLSvc" (confirmed SQL Servers)
    /// 2. Computer accounts with "SQL" in their name (naming convention heuristic)
    /// SQL Server registers SPNs in the format: MSSQLSvc/hostname:port or MSSQLSvc/hostname:instancename
    /// </para>
    /// 
    /// <para>
    /// <b>PowerShell equivalent:</b>
    /// <code>
    /// # Domain-only query (LDAP):
    /// ([adsisearcher]::new([adsi]"LDAP://corp.local", "(|(servicePrincipalName=MSSQL*)(&(objectCategory=computer)(cn=*SQL*)))")).FindAll()
    /// 
    /// # Forest-wide query (Global Catalog):
    /// ([adsisearcher]::new([adsi]"GC://corp.local", "(|(servicePrincipalName=MSSQL*)(&(objectCategory=computer)(cn=*SQL*)))")).FindAll()
    /// </code>
    /// </para>
    /// 
    /// <para>
    /// <b>Limitations:</b>
    /// - SPN-based discovery: Only finds SQL Servers with registered SPNs
    /// - Name-based discovery: May include false positives (MySQL, backup servers, etc.)
    /// - IP addresses are resolved via DNS, not stored in LDAP
    /// - lastLogonTimestamp has ~14 day replication delay
    /// </para>
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
            Logger.TaskNested("Discovery methods: MSSQLSvc SPNs + computers with 'SQL' in name.");
            
            if (forest)
            {
                Logger.TaskNested("Using Global Catalog (port 3268) to query all domains in the forest.");
            }

            // Initialize domain service based on the provided domain
            ADirectoryService domainService = new($"{protocol}://{domain}");
            LdapQueryService ldapService = new(domainService);

            // LDAP filter: Find objects with MSSQLSvc SPNs OR computers with "SQL" in name
            // This catches both properly configured SQL Servers and those without Kerberos SPNs
            const string ldapFilter = "(|(servicePrincipalName=MSSQL*)(&(objectCategory=computer)(cn=*SQL*)))";
            // Use lastLogonTimestamp instead of lastLogon - it's replicated across DCs
            string[] ldapAttributes = { "cn", "dnshostname", "samaccountname", "objectsid", "serviceprincipalname", "lastLogonTimestamp" };

            // Execute the LDAP query
            Dictionary<string, Dictionary<string, object[]>> ldapResults = ldapService.ExecuteQuery(ldapFilter, ldapAttributes);

            // Track unique servers with their instances
            var serverMap = new Dictionary<string, ServerInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (Dictionary<string, object[]> ldapEntry in ldapResults.Values)
            {
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
                if (ldapEntry.TryGetValue("lastlogontimestamp", out object[] logonValues) && logonValues?.Length > 0)
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

                // Check for MSSQLSvc SPNs
                bool hasSqlSpn = false;
                ldapEntry.TryGetValue("serviceprincipalname", out object[] spnValues);
                
                if (spnValues != null && spnValues.Length > 0)
                {
                    foreach (string spn in spnValues.Cast<string>())
                    {
                        if (!spn.StartsWith("MSSQLSvc", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        hasSqlSpn = true;
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
                                Instances = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                                DiscoveryMethod = "SPN"
                            };
                            serverMap[serverName] = serverInfo;
                        }

                        serverInfo.Instances.Add(instanceOrPort);
                    }
                }

                // If no SQL SPN found, this entry matched by computer name containing "SQL"
                if (!hasSqlSpn)
                {
                    // Get server name from dnshostname or cn
                    string serverName = null;
                    if (ldapEntry.TryGetValue("dnshostname", out object[] dnsValues) && dnsValues?.Length > 0)
                    {
                        serverName = dnsValues[0]?.ToString();
                    }
                    if (string.IsNullOrEmpty(serverName) && ldapEntry.TryGetValue("cn", out object[] cnValues) && cnValues?.Length > 0)
                    {
                        serverName = cnValues[0]?.ToString();
                    }

                    if (!string.IsNullOrEmpty(serverName) && !serverMap.ContainsKey(serverName))
                    {
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

                        serverMap[serverName] = new ServerInfo
                        {
                            ServerName = serverName,
                            IpAddress = serverIpAddress,
                            AccountName = accountName,
                            ObjectSid = objectSid,
                            LastLogon = lastLogonDate,
                            Instances = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "(name match)" },
                            DiscoveryMethod = "Name"
                        };
                    }
                }
            }

            if (serverMap.Count == 0)
            {
                Logger.Warning("No SQL Servers found.");
                return 0;
            }

            // Build output table
            DataTable resultTable = new();
            resultTable.Columns.Add("dnsHostName", typeof(string));
            resultTable.Columns.Add("IP (DNS)", typeof(string));
            resultTable.Columns.Add("Instances", typeof(string));
            resultTable.Columns.Add("Source", typeof(string));
            resultTable.Columns.Add("sAMAccountName", typeof(string));
            resultTable.Columns.Add("lastLogonTimestamp", typeof(string));

            foreach (var server in serverMap.Values.OrderBy(s => s.DiscoveryMethod).ThenBy(s => s.ServerName))
            {
                resultTable.Rows.Add(
                    server.ServerName,
                    server.IpAddress,
                    string.Join(", ", server.Instances.OrderBy(i => i)),
                    server.DiscoveryMethod,
                    server.AccountName,
                    server.LastLogon
                );
            }

            Console.WriteLine(OutputFormatter.ConvertDataTable(resultTable));

            int spnCount = serverMap.Values.Count(s => s.DiscoveryMethod == "SPN");
            int nameCount = serverMap.Values.Count(s => s.DiscoveryMethod == "Name");
            int totalInstances = serverMap.Values.Where(s => s.DiscoveryMethod == "SPN").Sum(s => s.Instances.Count);
            Logger.Success($"{serverMap.Count} unique SQL Server(s) found: {spnCount} via SPN ({totalInstances} instance(s)), {nameCount} via naming convention.");
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
            public string DiscoveryMethod { get; set; }
        }
    }
}
