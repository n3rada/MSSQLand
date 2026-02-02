// MSSQLand/Actions/Remote/AdsiQuery.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.Remote
{
    /// <summary>
    /// Performs LDAP queries against ADSI linked servers via SQL Server's OPENQUERY.
    ///
    /// Argument resolution is context-dependent and cannot use auto-binding:
    ///   adsiquery <fqdn> <ldap query>            → temp server, custom query
    ///   adsiquery <server> <fqdn> <ldap query>   → existing linked server, custom query
    /// </summary>
    internal class AdsiQuery : BaseAction
    {
        [ArgumentMetadata(Position = 0, Description = "ADSI linked server name (optional - creates temporary server if omitted)")]
        private string _adsiServerName = "";

        [ArgumentMetadata(Position = 1, Description = "FQDN of the target domain")]
        private string _domainFqdn = "";

        [ArgumentMetadata(Position = 2, Description = "LDAP query to execute against the ADSI server")]
        private string _ldapQuery = "";

        private bool _usingTempServer = false;

        public override void ValidateArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                throw new ArgumentException(
                    "FQDN and LDAP query are required.\n" +
                    "  Temp server:      adsiquery <fqdn> \"<ldap query>\"\n" +
                    "  Existing server:  adsiquery <server> <fqdn> \"<ldap query>\"");
            }

            BindArguments(args);

            // Argument resolution is positionally ambiguous — the first arg could be
            // a domain FQDN or a linked server name. Disambiguate by checking for a
            // dot: FQDNs contain dots, linked server names do not.
            if (args[0].Contains("."))
            {
                // adsiquery <fqdn> <ldap query>
                _usingTempServer = true;
                _domainFqdn = args[0];
                _adsiServerName = "";

                if (args.Length < 2)
                    throw new ArgumentException("LDAP query is required");

                _ldapQuery = string.Join(" ", args, 1, args.Length - 1);
            }
            else
            {
                // adsiquery <server> <fqdn> <ldap query>
                _adsiServerName = args[0];

                if (args.Length < 2)
                    throw new ArgumentException("FQDN is required");

                _domainFqdn = args[1];

                if (args.Length < 3)
                    throw new ArgumentException("LDAP query is required");

                _ldapQuery = string.Join(" ", args, 2, args.Length - 2);
            }

            if (string.IsNullOrWhiteSpace(_domainFqdn))
                throw new ArgumentException("Domain FQDN cannot be empty");

            if (string.IsNullOrWhiteSpace(_ldapQuery))
                throw new ArgumentException("LDAP query cannot be empty");
        }

        public override object Execute(DatabaseContext databaseContext)
        {
            // FQDN format warning here — ValidateArguments must not log
            if (!_domainFqdn.Contains("."))
            {
                Logger.Warning($"'{_domainFqdn}' does not appear to be a fully qualified domain name");
                return null;
            }

            AdsiService adsiService = new(databaseContext);
            bool cleanupRequired = false;

            try
            {
                if (_usingTempServer)
                {
                    _adsiServerName = $"ADSI_{Guid.NewGuid().ToString("N").Substring(0, 6)}";

                    Logger.Task($"Creating temporary ADSI linked server '{_adsiServerName}'");

                    if (!adsiService.CreateAdsiLinkedServer(_adsiServerName))
                    {
                        Logger.Error("Failed to create temporary ADSI linked server");
                        return null;
                    }

                    // Data access is off by default on new linked servers.
                    // Must be enabled before OPENQUERY will work.
                    if (!databaseContext.ConfigService.SetServerOption(_adsiServerName, "data access", "true"))
                    {
                        Logger.Error($"Failed to enable data access for '{_adsiServerName}'");
                        adsiService.DropLinkedServer(_adsiServerName);
                        return null;
                    }

                    Logger.Success($"Temporary ADSI linked server '{_adsiServerName}' created");
                    cleanupRequired = true;
                }
                else
                {
                    if (!adsiService.AdsiServerExists(_adsiServerName))
                    {
                        Logger.Error($"ADSI linked server '{_adsiServerName}' not found");
                        Logger.ErrorNested("Omit the server name to create a temporary one automatically");
                        return null;
                    }
                }

                Logger.Task($"Querying ADSI server '{_adsiServerName}'");
                Logger.TaskNested($"Domain: {_domainFqdn}");
                Logger.TaskNested($"LDAP: {_ldapQuery}");

                string query = $"SELECT * FROM OPENQUERY([{_adsiServerName}], '{EscapeSingleQuotes(_ldapQuery)}')";

                DataTable result;
                try
                {
                    result = databaseContext.QueryService.ExecuteTable(query);
                }
                catch (Exception ex) when (!_usingTempServer && ex.Message.Contains("data access for linked server"))
                {
                    // Only relevant for pre-existing linked servers where data access
                    // may have been disabled. Temp servers have it enabled at creation.
                    Logger.Warning($"Data access is disabled for '{_adsiServerName}'");
                    Logger.Info("Attempting to enable data access...");

                    if (!databaseContext.ConfigService.SetServerOption(_adsiServerName, "data access", "true"))
                    {
                        Logger.Error($"Failed to enable data access for '{_adsiServerName}'");
                        return null;
                    }

                    Logger.Success("Data access enabled, retrying query");
                    result = databaseContext.QueryService.ExecuteTable(query);
                }

                if (result.Rows.Count == 0)
                {
                    Logger.Warning("No results found");
                    return null;
                }

                Logger.Success($"Retrieved {result.Rows.Count} result{(result.Rows.Count > 1 ? "s" : "")}");
                Console.WriteLine(OutputFormatter.ConvertDataTable(result));

                return result;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to execute LDAP query: {ex.Message}");

                if (ex.Message.Contains("Access denied"))
                    Logger.Info("The SQL Server service account may lack permissions to query Active Directory");
                else if (ex.Message.Contains("Provider cannot be found"))
                    Logger.Info("The ADSDSOObject OLE DB provider is not available on this server");
                else if (ex.Message.Contains("syntax") || ex.Message.Contains("LDAP"))
                    Logger.Info($"Check LDAP query syntax. Current domain: {_domainFqdn}");

                return null;
            }
            finally
            {
                if (cleanupRequired && !string.IsNullOrEmpty(_adsiServerName))
                {
                    try
                    {
                        adsiService.DropLinkedServer(_adsiServerName);
                        Logger.Success($"Temporary ADSI linked server '{_adsiServerName}' cleaned up");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Failed to cleanup temporary linked server: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Converts a fully qualified domain name to an LDAP base DN path.
        /// e.g. "contoso.local" → "LDAP://DC=contoso,DC=local"
        /// </summary>
        private string BuildLdapPath(string domainFqdn)
        {
            string[] parts = domainFqdn.Split('.');
            return "LDAP://" + string.Join(",", Array.ConvertAll(parts, p => $"DC={p}"));
        }

        private string EscapeSingleQuotes(string input)
        {
            return input?.Replace("'", "''");
        }
    }
}