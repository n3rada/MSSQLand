using MSSQLand.Services;
using System;
using System.Collections.Generic;
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
        /// <param name="domain">The Active Directory domain to query (e.g., corp.com)</param>
        /// <returns>Number of SQL Servers found</returns>
        public static int Execute(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain))
            {
                throw new ArgumentException("Active Directory domain is required. Please provide a valid domain name (e.g., corp.com).");
            }

            Logger.TaskNested($"Lurking for MS SQL Servers on Active Directory domain: {domain}");

            // Initialize domain service based on the provided domain
            ADirectoryService domainService = new($"LDAP://{domain}");
            LdapQueryService ldapService = new(domainService);

            // LDAP filter and properties for MS SQL SPNs
            const string ldapFilter = "(&(sAMAccountType=805306368)(servicePrincipalName=MSSQL*))";
            string[] ldapAttributes = { "cn", "samaccountname", "objectsid", "serviceprincipalname", "lastlogon" };

            // Execute the LDAP query
            Dictionary<string, Dictionary<string, object[]>> ldapResults = ldapService.ExecuteQuery(ldapFilter, ldapAttributes);

            int sqlServerCount = 0;

            foreach (Dictionary<string, object[]> ldapEntry in ldapResults.Values)
            {
                foreach (string spn in ldapEntry["serviceprincipalname"].Cast<string>())
                {
                    int serviceDelimiterIndex = spn.IndexOf('/');

                    string serviceType = spn.Substring(0, serviceDelimiterIndex);
                    string serviceInstance = spn.Substring(serviceDelimiterIndex + 1);

                    int portDelimiterIndex = serviceInstance.IndexOf(':');
                    string serverName = portDelimiterIndex == -1
                        ? serviceInstance
                        : serviceInstance.Substring(0, portDelimiterIndex);

                    // Resolve the server's IP address
                    string serverIpAddress;
                    try
                    {
                        IPAddress[] ipAddresses = Dns.GetHostAddresses(serverName);
                        serverIpAddress = ipAddresses.Length > 0 ? ipAddresses[0].ToString() : "No IP found";
                    }
                    catch (Exception)
                    {
                        serverIpAddress = "Unable to resolve";
                    }

                    // Extract LDAP properties
                    string accountName = ldapEntry["samaccountname"][0].ToString();
                    string commonName = ldapEntry["cn"][0].ToString();

                    byte[] objectSidBytes = (byte[])ldapEntry["objectsid"][0];
                    string objectSid = new SecurityIdentifier(objectSidBytes, 0).ToString();

                    long lastLogonTimestamp = (long)ldapEntry["lastlogon"][0];
                    string lastLogonDate = DateTime.FromFileTime(lastLogonTimestamp).ToString("G");

                    Dictionary<string, string> spnDetails = new()
                    {
                        { "Server Name", serverName },
                        { "IP Address", serverIpAddress },
                        { "Service Instance", serviceInstance },
                        { "Object SID", objectSid },
                        { "Account Name", accountName },
                        { "Common Name", commonName },
                        { "Service Type", serviceType },
                        { "SPN", spn },
                        { "Last Logon", lastLogonDate }
                    };

                    // Output details as a Markdown table
                    Console.WriteLine(OutputFormatter.ConvertDictionary(spnDetails, "Property", "Value"));
                    sqlServerCount++;
                }
            }

            Logger.Success($"{sqlServerCount} MS SQL Server(s) found.");
            return sqlServerCount;
        }
    }
}
