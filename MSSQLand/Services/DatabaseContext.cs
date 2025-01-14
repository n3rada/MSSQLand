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
            ConfigService = new ConfigurationService(QueryService);
            UserService = new UserService(QueryService);

            // Perform user impersonation if specified
            HandleImpersonation();
        }


        /// <summary>
        /// Handles user impersonation if specified in the target.
        /// </summary>
        private void HandleImpersonation()
        {
            string impersonateTarget = Server.ImpersonationUser;
            if (!string.IsNullOrEmpty(impersonateTarget))
            {
                if (UserService.CanImpersonate(impersonateTarget))
                {
  
                    UserService.ImpersonateUser(impersonateTarget);
                    Logger.Success($"Successfully impersonated user: {impersonateTarget}");
                }
                else
                {
                    throw new InvalidOperationException($"Cannot impersonate user: {impersonateTarget}");

                }
            }
        }

    }
}
