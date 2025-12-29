using MSSQLand.Utilities;
using System;
using MSSQLand.Models;

namespace MSSQLand.Services
{
    public class DatabaseContext
    {
        public readonly AuthenticationService AuthService;

        public readonly Server Server;
        public readonly QueryService QueryService;
        public readonly ConfigurationService ConfigService;
        public readonly UserService UserService;


        public DatabaseContext(AuthenticationService authService)
        {
            AuthService = authService;
            Server = AuthService.Server;
            QueryService = new QueryService(AuthService.Connection);
            // Use authenticated Server as ExecutionServer (direct reference, version already set)
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
        /// </summary>
        private bool HandleImpersonation()
        {
            string impersonateTarget = Server.ImpersonationUser;
            if (!string.IsNullOrEmpty(impersonateTarget))
            {
                if (UserService.CanImpersonate(impersonateTarget))
                {
  
                    UserService.ImpersonateUser(impersonateTarget);
                    Logger.Success($"Successfully impersonated user: {impersonateTarget}");
                    return true;
                }

                Logger.Error($"Cannot impersonate user: {impersonateTarget}");
                return false;
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
        /// Creates a deep copy of the current DatabaseContext while keeping the same connection.
        /// This means that any **impersonation** or **persistent session modifications** 
        /// (e.g., `EXECUTE AS`, temp table changes, transaction states) 
        /// will also be reflected in the copied context.
        /// Use cautiously when dealing with impersonation-sensitive operations.
        /// </summary>
        /// <returns>A copied DatabaseContext instance with the same connection.</returns>
        public DatabaseContext Copy()
        {
            Logger.Debug("Creating a deep copy of DatabaseContext while keeping the same connection.");

            // Create a new DatabaseContext instance but keep the same connection
            DatabaseContext copiedContext = new(this.AuthService);

            // Deep Copy LinkedServers to avoid shared state
            copiedContext.QueryService.LinkedServers = new LinkedServers(this.QueryService.LinkedServers);

            return copiedContext;
        }
    }
}
