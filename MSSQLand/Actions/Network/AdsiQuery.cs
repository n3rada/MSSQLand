using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.Network
{
    /// <summary>
    /// Performs LDAP queries against ADSI linked servers.
    /// Allows querying Active Directory objects through SQL Server's OPENQUERY.
    /// </summary>
    internal class AdsiQuery : BaseAction
    {
        [ArgumentMetadata(Position = 0, Description = "ADSI server name (optional - creates temporary server if omitted)")]
        private string _adsiServerName;

        [ArgumentMetadata(Position = 1, Description = "LDAP query string or preset (users, computers, groups, admins, ou, all)")]
        private string _ldapQuery;

        [ArgumentMetadata(Position = 2, Description = "Quick query preset: users, computers, groups, admins, ou, or custom (default: users)")]
        private string _preset = "users";

        [ArgumentMetadata(Position = 3, Description = "Fully qualified domain name (e.g., contoso.local) - required for presets")]
        private string _domainFqdn;

        private bool _usingTempServer = false;

        public override void ValidateArguments(string additionalArguments)
        {
            if (string.IsNullOrWhiteSpace(additionalArguments))
            {
                // No arguments - need domain for default preset
                throw new ArgumentException("Fully qualified domain name is required. Example: /a:adsiquery contoso.local");
            }

            string[] parts = SplitArguments(additionalArguments);

            // Check if first argument is a preset (means no server name provided)
            if (IsPreset(parts[0].ToLower()))
            {
                // No server name provided, use temporary server
                _usingTempServer = true;
                _preset = parts[0].ToLower();

                // Domain must be provided
                if (parts.Length < 2)
                {
                    throw new ArgumentException($"Fully qualified domain name is required. Example: /a:adsiquery {_preset} contoso.local");
                }

                _domainFqdn = parts[1];

                // Check if there's a custom query after the domain
                if (parts.Length > 2)
                {
                    _preset = "custom";
                    _ldapQuery = string.Join(" ", parts, 2, parts.Length - 2);
                }
            }
            else
            {
                // First argument could be a domain (no server name) or server name
                // Try to determine if it looks like a domain (contains a dot)
                if (parts[0].Contains("."))
                {
                    // Likely a domain FQDN, use temp server
                    _usingTempServer = true;
                    _domainFqdn = parts[0];
                    _preset = "users"; // Default preset

                    // Check if second argument is a preset
                    if (parts.Length > 1 && IsPreset(parts[1].ToLower()))
                    {
                        _preset = parts[1].ToLower();
                    }
                }
                else
                {
                    // First argument is the ADSI server name
                    _adsiServerName = parts[0];

                    // Domain must be provided as second argument
                    if (parts.Length < 2)
                    {
                        throw new ArgumentException($"Fully qualified domain name is required. Example: /a:adsiquery {_adsiServerName} contoso.local users");
                    }

                    _domainFqdn = parts[1];

                    // Third argument can be either a preset or a custom LDAP query
                    if (parts.Length > 2)
                    {
                        string thirdArg = parts[2].ToLower();

                        // Check if it's a preset
                        if (IsPreset(thirdArg))
                        {
                            _preset = thirdArg;
                        }
                        else
                        {
                            // Treat as custom LDAP query
                            _preset = "custom";
                            _ldapQuery = string.Join(" ", parts, 2, parts.Length - 2);
                        }
                    }
                }
            }

            // Validate domain format
            if (string.IsNullOrWhiteSpace(_domainFqdn))
            {
                throw new ArgumentException("Domain FQDN cannot be empty.");
            }

            if (!_domainFqdn.Contains("."))
            {
                Logger.Warning($"Domain '{_domainFqdn}' does not appear to be a fully qualified domain name (FQDN).");
                Logger.InfoNested("Example of FQDN: contoso.local, corp.company.com");
            }
        }

        private bool IsPreset(string arg)
        {
            return arg == "users" || arg == "computers" || arg == "groups" || 
                   arg == "admins" || arg == "ou" || arg == "all";
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            AdsiService adsiService = new(databaseContext);
            bool cleanupRequired = false;

            try
            {
                // Handle temporary ADSI server creation if needed
                if (_usingTempServer)
                {
                    _adsiServerName = $"ADSI-{Guid.NewGuid().ToString("N").Substring(0, 6)}";
                    
                    Logger.Task($"Creating temporary ADSI server '{_adsiServerName}'");

                    if (!adsiService.CreateAdsiLinkedServer(_adsiServerName))
                    {
                        Logger.Error("Failed to create temporary ADSI server.");
                        return null;
                    }

                    Logger.Success($"Temporary ADSI server '{_adsiServerName}' created");
                    cleanupRequired = true;
                }
                else
                {
                    // Verify the specified ADSI server exists
                    if (!adsiService.AdsiServerExists(_adsiServerName))
                    {
                        Logger.Error($"ADSI linked server '{_adsiServerName}' not found.");
                        Logger.InfoNested("Use '/a:adsi list' to see available ADSI servers or omit the server name to create a temporary one.");
                        return null;
                    }
                }

                Logger.Task($"Querying ADSI server '{_adsiServerName}'");

                // Build the LDAP query based on preset or custom input
                string ldapQuery = _preset == "custom" ? _ldapQuery : BuildPresetQuery();

                Logger.InfoNested($"Domain: {_domainFqdn}");
                Logger.InfoNested($"Preset: {_preset}");
                Logger.InfoNested($"LDAP Query: {ldapQuery}");

                // Execute the OPENQUERY against the ADSI server
                string query = $"SELECT * FROM OPENQUERY([{_adsiServerName}], '{EscapeSingleQuotes(ldapQuery)}')";
                
                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result.Rows.Count == 0)
                {
                    Logger.Warning("No results found.");
                    return null;
                }

                Logger.Success($"Retrieved {result.Rows.Count} result{(result.Rows.Count > 1 ? "s" : "")}");

                // Display the results
                Console.WriteLine(OutputFormatter.ConvertDataTable(result));

                return result;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to execute LDAP query: {ex.Message}");
                
                // Provide helpful error messages
                if (ex.Message.Contains("Access denied"))
                {
                    Logger.InfoNested("The SQL Server service account may not have permissions to query Active Directory.");
                }
                else if (ex.Message.Contains("Provider cannot be found"))
                {
                    Logger.InfoNested("The ADSDSOObject provider may not be available on this server.");
                }
                else if (ex.Message.Contains("syntax") || ex.Message.Contains("LDAP"))
                {
                    Logger.InfoNested("Check your LDAP query syntax and domain FQDN.");
                    Logger.InfoNested($"Current domain: {_domainFqdn}");
                    Logger.InfoNested("Example: /a:adsiquery contoso.local users");
                }

                return null;
            }
            finally
            {
                // Cleanup temporary ADSI server if it was created
                if (cleanupRequired && !string.IsNullOrEmpty(_adsiServerName))
                {
                    Logger.InfoNested($"Cleaning up temporary ADSI server '{_adsiServerName}'");
                    try
                    {
                        adsiService.DropLinkedServer(_adsiServerName);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Failed to cleanup temporary server: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Builds an LDAP query based on the selected preset using the provided domain FQDN.
        /// </summary>
        private string BuildPresetQuery()
        {
            // Convert domain FQDN to LDAP path (e.g., contoso.local -> DC=contoso,DC=local)
            string ldapPath = BuildLdapPath(_domainFqdn);

            return _preset switch
            {
                "users" => $"SELECT cn, sAMAccountName, distinguishedName, whenCreated FROM '{ldapPath}' WHERE objectClass='user' AND objectCategory='person'",
                
                "computers" => $"SELECT cn, dNSHostName, operatingSystem, operatingSystemVersion FROM '{ldapPath}' WHERE objectClass='computer'",
                
                "groups" => $"SELECT cn, sAMAccountName, distinguishedName, groupType FROM '{ldapPath}' WHERE objectClass='group'",
                
                "admins" => $"SELECT cn, sAMAccountName, distinguishedName FROM '{ldapPath}' WHERE objectClass='user' AND (memberOf='CN=Domain Admins,CN=Users,{ldapPath.Replace("LDAP://", "")}' OR memberOf='CN=Enterprise Admins,CN=Users,{ldapPath.Replace("LDAP://", "")}')",
                
                "ou" => $"SELECT ou, name, distinguishedName FROM '{ldapPath}' WHERE objectClass='organizationalUnit'",
                
                "all" => $"SELECT * FROM '{ldapPath}'",
                
                _ => $"SELECT cn, distinguishedName FROM '{ldapPath}' WHERE objectClass='user'"
            };
        }

        /// <summary>
        /// Converts a fully qualified domain name to an LDAP path.
        /// Example: contoso.local -> LDAP://DC=contoso,DC=local
        /// </summary>
        private string BuildLdapPath(string domainFqdn)
        {
            string[] parts = domainFqdn.Split('.');
            string ldapPath = "LDAP://";
            
            for (int i = 0; i < parts.Length; i++)
            {
                ldapPath += $"DC={parts[i]}";
                if (i < parts.Length - 1)
                    ldapPath += ",";
            }
            
            return ldapPath;
        }

        /// <summary>
        /// Escapes single quotes in LDAP queries for SQL Server.
        /// </summary>
        private string EscapeSingleQuotes(string input)
        {
            return input?.Replace("'", "''");
        }
    }
}
