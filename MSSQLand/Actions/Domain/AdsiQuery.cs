// MSSQLand/Actions/Remote/AdsiQuery.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.Domain
{
    /// <summary>
    /// Performs LDAP queries against ADSI linked servers via SQL Server's OPENQUERY.
    ///
    /// Usage:
    ///   adsi-query <server> "<ldap query>"
    ///
    /// LDAP query format (ADsDSOObject SQL dialect):
    ///   SELECT [ALL] * | select-list FROM 'ADsPath' [WHERE search-condition] [ORDER BY sort-list]
    /// Notes:
    ///   - ADsPath must be wrapped in single quotes.
    ///   - ORDER BY supports a single sort key in AD.
    ///   - Joins are not supported by the ADsDSOObject provider.
    /// Reference: https://learn.microsoft.com/en-us/windows/win32/adsi/sql-dialect
    ///
    /// LDAP query examples:
    ///   SELECT ADsPath, cn FROM 'LDAP://DC=pgd,DC=lab' WHERE objectCategory='group'
    ///   SELECT cn,sAMAccountName FROM 'LDAP://DC=pgd,DC=lab' WHERE objectClass='user'
    ///   SELECT cn FROM 'LDAP://OU=Workstations,DC=pgd,DC=lab' WHERE sAMAccountName='alice'
    /// </summary>
    internal class AdsiQuery : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "ADSI linked server name")]
        private string _adsiServerName = "";

        [ArgumentMetadata(Position = 1, Required = true, Remainder = true, Description = "LDAP query to execute against the ADSI server")]
        private string _ldapQuery = "";

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);

            if (args == null || args.Length < 2)
                throw new ArgumentException(
                    "Server name and LDAP query are required.\n" +
                    "  Usage: adsi-query <server> \"<ldap query>\"");

            if (string.IsNullOrWhiteSpace(_adsiServerName))
                throw new ArgumentException("ADSI server name cannot be empty");

            if (string.IsNullOrWhiteSpace(_ldapQuery))
                throw new ArgumentException("LDAP query cannot be empty");
        }

        public override object Execute(DatabaseContext databaseContext)
        {
            AdsiService adsiService = new(databaseContext);

            try
            {
                if (!adsiService.AdsiServerExists(_adsiServerName))
                {
                    Logger.Error($"ADSI linked server '{_adsiServerName}' not found");
                    return null;
                }

                Logger.Task($"Querying ADSI server '{_adsiServerName}'");
                Logger.TaskNested($"LDAP: {_ldapQuery}");

                DataTable result;
                try
                {
                    result = adsiService.ExecuteRawLdapQuery(_ldapQuery, _adsiServerName);
                }
                catch (Exception ex) when (ex.Message.Contains("data access for linked server"))
                {
                    // Data access may have been disabled on the linked server.
                    Logger.Warning($"Data access is disabled for '{_adsiServerName}'");
                    Logger.Info("Attempting to enable data access...");

                    if (!databaseContext.ConfigService.SetServerOption(_adsiServerName, "data access", "true"))
                    {
                        Logger.Error($"Failed to enable data access for '{_adsiServerName}'");
                        return null;
                    }

                    Logger.Success("Data access enabled, retrying query");
                    result = adsiService.ExecuteRawLdapQuery(_ldapQuery, _adsiServerName);
                }

                if (result.Rows.Count == 0)
                {
                    Logger.Warning("No results found");
                    return null;
                }

                Console.WriteLine(OutputFormatter.ConvertDataTable(result));

                Logger.Success($"Retrieved {result.Rows.Count} result{(result.Rows.Count > 1 ? "s" : "")}");

                return result;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to execute LDAP query: {ex.Message}");

                if (ex.Message.Contains("Access denied"))
                    Logger.Error("The SQL Server service account may lack permissions to query Active Directory");
                else if (ex.Message.Contains("Provider cannot be found"))
                    Logger.Error("The ADSDSOObject OLE DB provider is not available on this server");
                else if (ex.Message.Contains("syntax") || ex.Message.Contains("LDAP"))
                    Logger.Error("Check LDAP query syntax.");

                return null;
            }
        }

    }
}