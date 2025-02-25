using MSSQLand.Utilities;
using System;
using MSSQLand.Models;
using System.Data.SqlClient;


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
            ConfigService = new ConfigurationService(QueryService, Server);
            UserService = new UserService(QueryService);


            Server.Hostname = QueryService.ExecutionServer;

            if (HandleImpersonation() == false)
            {
                Environment.Exit(1);
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
    }
}
