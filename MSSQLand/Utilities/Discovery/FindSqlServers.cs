using MSSQLand.Services;
using MSSQLand.Utilities.Formatters;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
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
    public static class FindSqlServers
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

            if (forest)
            {
                Logger.TaskNested("Discovery methods: MSSQLSvc SPNs + computers with 'SQL' in name or description.");
                Logger.TaskNested("Using Global Catalog (port 3268) - OU-based search disabled (not indexed in GC).");
            }
            else
            {
                Logger.TaskNested("Discovery methods: MSSQLSvc SPNs + computers with 'SQL' in name, description, or OU.");
            }

            // Initialize domain service based on the provided domain
            ADirectoryService domainService = new($"{protocol}://{domain}");
            LdapQueryService ldapService = new(domainService);

            // LDAP filter: Find objects with MSSQLSvc SPNs OR computers with "SQL" in name or description
            // OU-based searching only works with LDAP, not GC (distinguishedName is not indexed in Global Catalog)
            string ldapFilter = forest
                ? "(|(servicePrincipalName=MSSQLSvc*)(&(objectCategory=computer)(cn=*SQL*))(&(objectCategory=computer)(description=*SQL*)))"
                : "(|(servicePrincipalName=MSSQLSvc*)(&(objectCategory=computer)(cn=*SQL*))(&(objectCategory=computer)(description=*SQL*))(&(objectCategory=computer)(distinguishedName=*OU=*SQL*)))";

            // Use lastLogonTimestamp instead of lastLogon - it's replicated across DCs
            string[] ldapAttributes = { "cn", "dnshostname", "samaccountname", "objectsid", "serviceprincipalname", "lastLogonTimestamp", "description", "distinguishedName" };

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

                string description = "";
                if (ldapEntry.TryGetValue("description", out object[] descValues) && descValues?.Length > 0)
                {
                    description = descValues[0]?.ToString() ?? "";
                }

                string distinguishedName = "";
                if (ldapEntry.TryGetValue("distinguishedname", out object[] dnValues) && dnValues?.Length > 0)
                {
                    distinguishedName = dnValues[0]?.ToString() ?? "";
                }

                string cn = "";
                if (ldapEntry.TryGetValue("cn", out object[] cnVals) && cnVals?.Length > 0)
                {
                    cn = cnVals[0]?.ToString() ?? "";
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
                            serverInfo = new ServerInfo
                            {
                                ServerName = serverName,
                                AccountName = accountName,
                                ObjectSid = objectSid,
                                LastLogon = lastLogonDate,
                                Description = description,
                                Instances = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                                DiscoveryMethod = "SPN"
                            };
                            serverMap[serverName] = serverInfo;
                        }

                        serverInfo.Instances.Add(instanceOrPort);
                    }
                }

                // If no SQL SPN found, determine which condition matched
                if (!hasSqlSpn)
                {
                    // Get server name from dnshostname or cn
                    string serverName = null;
                    if (ldapEntry.TryGetValue("dnshostname", out object[] dnsValues) && dnsValues?.Length > 0)
                    {
                        serverName = dnsValues[0]?.ToString();
                    }
                    if (string.IsNullOrEmpty(serverName) && !string.IsNullOrEmpty(cn))
                    {
                        serverName = cn;
                    }

                    if (!string.IsNullOrEmpty(serverName) && !serverMap.ContainsKey(serverName))
                    {
                        // Determine discovery method based on what matched
                        string discoveryMethod = DetermineDiscoveryMethod(cn, description, distinguishedName);

                        serverMap[serverName] = new ServerInfo
                        {
                            ServerName = serverName,
                            AccountName = accountName,
                            ObjectSid = objectSid,
                            LastLogon = lastLogonDate,
                            Description = description,
                            Instances = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                            DiscoveryMethod = discoveryMethod
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
            resultTable.Columns.Add("Description", typeof(string));
            resultTable.Columns.Add("Instances", typeof(string));
            resultTable.Columns.Add("Source", typeof(string));
            resultTable.Columns.Add("sAMAccountName", typeof(string));
            resultTable.Columns.Add("lastLogonTimestamp", typeof(string));

            foreach (var server in serverMap.Values
                .OrderBy(s => {
                    int dotIndex = s.ServerName.IndexOf('.');
                    return dotIndex > 0 ? s.ServerName.Substring(dotIndex + 1) : string.Empty;
                })
                .ThenBy(s => s.ServerName))
            {
                resultTable.Rows.Add(
                    server.ServerName,
                    server.Description ?? "",
                    string.Join(", ", server.Instances.OrderBy(i => i)),
                    server.DiscoveryMethod,
                    server.AccountName,
                    server.LastLogon
                );
            }

            Console.WriteLine(OutputFormatter.ConvertDataTable(resultTable));

            int spnCount = serverMap.Values.Count(s => s.DiscoveryMethod == "SPN");
            int heuristicCount = serverMap.Values.Count(s => s.DiscoveryMethod != "SPN");
            int totalInstances = serverMap.Values.Where(s => s.DiscoveryMethod == "SPN").Sum(s => s.Instances.Count);
            Logger.Success($"{serverMap.Count} unique SQL Server(s) found: {spnCount} via SPN ({totalInstances} instance(s)), {heuristicCount} via heuristics.");
            return serverMap.Count;
        }

        /// <summary>
        /// Determines how a computer was discovered based on its properties.
        /// </summary>
        private static string DetermineDiscoveryMethod(string cn, string description, string distinguishedName)
        {
            var methods = new List<string>();

            if (!string.IsNullOrEmpty(cn) && cn.IndexOf("SQL", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                methods.Add("Name");
            }

            if (!string.IsNullOrEmpty(description) && description.IndexOf("SQL", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                methods.Add("Desc");
            }

            // Check if any OU in the DN contains "SQL"
            if (!string.IsNullOrEmpty(distinguishedName) && distinguishedName.IndexOf("OU=", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Extract OU portions and check for SQL
                foreach (string part in distinguishedName.Split(','))
                {
                    if (part.Trim().StartsWith("OU=", StringComparison.OrdinalIgnoreCase) &&
                        part.IndexOf("SQL", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        methods.Add("OU");
                        break;
                    }
                }
            }

            return methods.Count > 0 ? string.Join("+", methods) : "Unknown";
        }

        private class ServerInfo
        {
            public string ServerName { get; set; }
            public string AccountName { get; set; }
            public string ObjectSid { get; set; }
            public string LastLogon { get; set; }
            public string Description { get; set; } = "";
            public HashSet<string> Instances { get; set; }
            public string DiscoveryMethod { get; set; }
        }
    }
}
