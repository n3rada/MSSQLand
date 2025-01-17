using MSSQLand.Utilities;
using MSSQLand.Services.Credentials;
using System;
using System.Data.SqlClient;
using System.Data;
using MSSQLand.Models;


namespace MSSQLand.Services
{
    public class DatabaseContext
    {
        private readonly AuthenticationService _authService;

        public readonly Server Server;
        public readonly QueryService QueryService;
        public readonly ConfigurationService ConfigService;
        public readonly UserService UserService;


        public DatabaseContext(AuthenticationService authService)
        {
            _authService = authService;
            Server = _authService.Server;
            QueryService = new QueryService(_authService.Connection);
            ConfigService = new ConfigurationService(QueryService, Server);
            UserService = new UserService(QueryService);


            Server.Hostname = QueryService.ExecutionServer;

            (string userName, string systemUser) = UserService.GetInfo();

            Logger.Info($"Logged in on {QueryService.ExecutionServer} as {systemUser}");
            Logger.InfoNested($"Mapped to the user {userName} ");

            // Perform user impersonation if specified
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
                else
                {
                    Logger.Error($"Cannot impersonate user: {impersonateTarget}");
                    return false;

                }
            }

            return true;
        }

    }
}
