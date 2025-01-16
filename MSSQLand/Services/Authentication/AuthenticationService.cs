using MSSQLand.Models;
using MSSQLand.Services.Credentials;
using System;
using System.Data.SqlClient;

namespace MSSQLand.Services
{
    public class AuthenticationService : IDisposable
    {
        public SqlConnection Connection { get; private set; }
        public readonly Server Server;

        public AuthenticationService(Server server)
        {
            Server = server;
        }

        /// <summary>
        /// Authenticates and establishes a connection to the database using the specified credentials type.
        /// </summary>
        /// <param name="credentialsType">The type of credentials to use (e.g., "token", "domain").</param>
        /// <param name="sqlServer">The SQL server address.</param>
        /// <param name="database">The target database.</param>
        /// <param name="username">The username (optional).</param>
        /// <param name="password">The password (optional).</param>
        /// <param name="domain">The domain for Windows authentication (optional).</param>
        public bool Authenticate(
            string credentialsType,
            string sqlServer,
            string database = "master",
            string username = null,
            string password = null,
            string domain = null)
        {
            try
            {
                // Get the appropriate credentials service
                var credentials = CredentialsFactory.GetCredentials(credentialsType);

                // Use the credentials service to authenticate and establish the connection
                Connection = credentials.Authenticate(sqlServer, database, username, password, domain);

                Server.Hostname = Connection.WorkstationId;
                Server.Version = Connection.ServerVersion;

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (Connection != null)
            {
                Connection.Close();
                Connection.Dispose();
                Connection = null;
            }
        }
    }
}
