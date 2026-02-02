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
    /// Argument resolution:
    ///   adsiquery "<ldap query>"                 → temp server
    ///   adsiquery "<ldap query>" --server <name> → existing server
    ///
    /// LDAP query format (ADsDSOObject SQL dialect):
    ///   SELECT [ALL] * | select-list FROM 'ADsPath' [WHERE search-condition] [ORDER BY sort-list]
    /// Notes:
    ///   - ADsPath must be wrapped in single quotes.
    ///   - ORDER BY supports a single sort key in AD.
    ///   - Joins are not supported by the ADsDSOObject provider.
    ///   - Escape special characters in filters using ADSI escape sequences (see link below).
    /// Reference: https://learn.microsoft.com/en-us/windows/win32/adsi/sql-dialect
    ///
    /// LDAP query examples:
    ///   SELECT ADsPath, cn FROM 'LDAP://DC=contoso,DC=local' WHERE objectCategory='group'
    ///   SELECT cn,sAMAccountName FROM 'LDAP://DC=contoso,DC=local' WHERE objectClass='user'
    ///   SELECT cn FROM 'LDAP://OU=Workstations,DC=contoso,DC=local' WHERE sAMAccountName='alice'
    /// </summary>
    internal class AdsiQuery : BaseAction
    {
        [ArgumentMetadata(LongName = "server", Required = false, Description = "ADSI linked server name (creates temporary server if omitted)")]
        private string _adsiServerName = "";

        [ArgumentMetadata(Position = 0, LongName = "ldap", Description = "LDAP query to execute against the ADSI server")]
        private string _ldapQuery = "";

        private bool _usingTempServer = false;

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);

            if (args == null || args.Length == 0)
            {
                throw new ArgumentException(
                    "LDAP query is required.\n" +
                    "  Temp server:      adsiquery \"<ldap query>\"\n" +
                    "  Existing server:  adsiquery \"<ldap query>\" --server <name>");
            }

            bool hasExplicitLdap = !string.IsNullOrWhiteSpace(_ldapQuery);
            bool hasExplicitServer = !string.IsNullOrWhiteSpace(_adsiServerName);

            if (!hasExplicitLdap)
            {
                throw new ArgumentException(
                    "Provide LDAP query as the first argument (and optionally --server <name>)."
                );
            }

            _usingTempServer = !hasExplicitServer;

            if (string.IsNullOrWhiteSpace(_ldapQuery))
                throw new ArgumentException("LDAP query cannot be empty");
        }

        public override object Execute(DatabaseContext databaseContext)
        {
            AdsiService adsiService = new(databaseContext);
            bool cleanupRequired = false;

            try
            {
                if (_usingTempServer)
                {
                    Logger.Task("Creating temporary ADSI linked server");

                    if (!adsiService.CreateAdsiLinkedServer(out _adsiServerName))
                    {
                        Logger.Error("Failed to create temporary ADSI linked server");
                        return null;
                    }

                    Logger.TaskNested($"Server name: {_adsiServerName}");

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
                Logger.TaskNested($"LDAP: {_ldapQuery}");

                string escapedLdapQuery = _ldapQuery?.Replace("'", "''");
                string query = $"SELECT * FROM OPENQUERY([{_adsiServerName}], '{escapedLdapQuery}')";

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
                    Logger.Info("Check LDAP query syntax.");

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

    }
}