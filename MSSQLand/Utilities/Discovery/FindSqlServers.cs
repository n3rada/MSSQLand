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
    /// Enumerates SQL Servers in Active Directory via LDAP.
    /// Discovers servers by MSSQLSvc SPNs (e.g., MSSQLSvc/hostname:port) and computers with "SQL" in name, description, or OU.
    /// <code>
    /// # PowerShell equivalent:
    /// ([adsisearcher]::new([adsi]"GC://corp.local", "(servicePrincipalName=MSSQLSvc*)")).FindAll() | % { $_.Properties.GetEnumerator() | % { "$($_.Name) = $($_.Value -join ', ')" } }
    /// </code>
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

                // Get dnshostname for FQDN (preferred over parsing from SPN)
                string dnsHostName = null;
                if (ldapEntry.TryGetValue("dnshostname", out object[] dnsValues) && dnsValues?.Length > 0)
                {
                    dnsHostName = dnsValues[0]?.ToString();
                }

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

                        string instanceOrPort = portDelimiterIndex == -1
                            ? "default"
                            : serviceInstance.Substring(portDelimiterIndex + 1);

                        // Use dnshostname (FQDN) if available, otherwise fall back to SPN hostname
                        string serverName = !string.IsNullOrEmpty(dnsHostName)
                            ? dnsHostName
                            : (portDelimiterIndex == -1 ? serviceInstance : serviceInstance.Substring(0, portDelimiterIndex));

                        // Key by objectSid to prevent duplicates when dnshostname is not available
                        if (!serverMap.TryGetValue(objectSid, out ServerInfo serverInfo))
                        {
                            serverInfo = new ServerInfo
                            {
                                ServerName = serverName,
                                AccountName = accountName,
                                ObjectSid = objectSid,
                                LastLogon = lastLogonDate,
                                Description = description,
                                Instances = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                            };
                            serverMap[objectSid] = serverInfo;
                        }
                        // Update server name if we now have a better one (FQDN)
                        else if (!string.IsNullOrEmpty(dnsHostName) && !serverInfo.ServerName.Contains("."))
                        {
                            serverInfo.ServerName = dnsHostName;
                        }

                        serverInfo.Instances.Add(instanceOrPort);
                    }
                }

                // If no SQL SPN found, determine which condition matched
                if (!hasSqlSpn)
                {
                    // Use dnshostname (already extracted above) or fall back to cn
                    string serverName = !string.IsNullOrEmpty(dnsHostName) ? dnsHostName : cn;

                    if (!string.IsNullOrEmpty(serverName) && !serverMap.ContainsKey(objectSid))
                    {
                        serverMap[objectSid] = new ServerInfo
                        {
                            ServerName = serverName,
                            AccountName = accountName,
                            ObjectSid = objectSid,
                            LastLogon = lastLogonDate,
                            Description = description,
                            Instances = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
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
                    server.AccountName,
                    server.LastLogon
                );
            }

            Console.WriteLine(OutputFormatter.ConvertDataTable(resultTable));

            int withInstances = serverMap.Values.Count(s => s.Instances.Count > 0);
            Logger.Success($"{serverMap.Count} SQL Server(s) found, {withInstances} with known instances.");
            return serverMap.Count;
        }

        private class ServerInfo
        {
            public string ServerName { get; set; }
            public string AccountName { get; set; }
            public string ObjectSid { get; set; }
            public string LastLogon { get; set; }
            public string Description { get; set; } = "";
            public HashSet<string> Instances { get; set; }
        }
    }
}
