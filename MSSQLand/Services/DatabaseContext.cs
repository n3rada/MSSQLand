// MSSQLand/Services/DatabaseContext.cs

using MSSQLand.Utilities;
using MSSQLand.Models;

using System;

namespace MSSQLand.Services
{
    public class DatabaseContext : IDisposable
    {
        public readonly AuthenticationService AuthService;

        public readonly Server Server;
        public readonly QueryService QueryService;
        public readonly ConfigurationService ConfigService;
        public readonly UserService UserService;

        private bool _disposed = false;

        public DatabaseContext(AuthenticationService authService)
        {
            AuthService = authService;
            Server = AuthService.Server;
            QueryService = new QueryService(AuthService.Connection);
            // Replace ExecutionServer with authenticated Server (hostname from @@SERVERNAME, version set)
            QueryService.ExecutionServer = Server;
            ConfigService = new ConfigurationService(QueryService, Server);
            UserService = new UserService(QueryService);

            if (HandleImpersonation() == false)
            {
                throw new Exception("Failed to handle impersonation. Exiting.");
            };
        }

        /// <summary>
        /// Handles user impersonation if specified in the target.
        /// Supports cascading impersonation (executing multiple EXECUTE AS LOGIN statements in sequence).
        /// </summary>
        private bool HandleImpersonation()
        {
            string[] impersonationUsers = Server.ImpersonationUsers;
            if (impersonationUsers != null && impersonationUsers.Length > 0)
            {
                int totalUsers = impersonationUsers.Length;
                for (int i = 0; i < totalUsers; i++)
                {
                    string user = impersonationUsers[i];
                    int position = i + 1;

                    try
                    {
                        UserService.ImpersonateUser(user);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to impersonate '{user}' at step {position}/{totalUsers}: {ex.Message}");
                        return false;
                    }
                }

                return true;
            }

            return true;
        }

        /// <summary>
        /// Creates a new instance of DatabaseContext with a fresh QueryService and SqlConnection.
        /// </summary>
        /// <returns>A new DatabaseContext instance with a fresh QueryService.</returns>
        public DatabaseContext Duplicate()
        {
            // Create a new AuthenticationService using the same Server instance
            AuthenticationService freshAuthService = AuthService.Duplicate();

            // Return a new DatabaseContext with the fresh AuthenticationService
            DatabaseContext newDatabaseContext = new (freshAuthService);

            // Deep Copy LinkedServers to avoid shared modifications
            newDatabaseContext.QueryService.LinkedServers = new LinkedServers(this.QueryService.LinkedServers);

            return newDatabaseContext;
        }

        /// <summary>
        /// Releases resources used by this DatabaseContext.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                AuthService?.Dispose();
            }

            _disposed = true;
        }
    }
}
