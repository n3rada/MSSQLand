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

        // Store authentication parameters for re-authentication
        private string _credentialsType;
        private string _sqlServer;
        private string _database;
        private string _username;
        private string _password;
        private string _domain;

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
            // Store authentication parameters
            _credentialsType = credentialsType;
            _sqlServer = sqlServer;
            _database = database;
            _username = username;
            _password = password;
            _domain = domain;

            // Get the appropriate credentials service
            var credentials = CredentialsFactory.GetCredentials(credentialsType);

            // Use the credentials service to authenticate and establish the connection
            Connection = credentials.Authenticate(sqlServer, database, username, password, domain);

            if (Connection == null)
            {
                return false;
            }

            Server.Version = Connection.ServerVersion;

            return true;
        }

        /// <summary>
        /// Generates a new SqlConnection using the stored parameters.
        /// </summary>
        /// <returns>A new SqlConnection object.</returns>
        public SqlConnection GetNewSqlConnection()
        {
            if (string.IsNullOrEmpty(_credentialsType) || string.IsNullOrEmpty(_sqlServer))
            {
                throw new InvalidOperationException("Authentication parameters are missing. Authenticate must be called first.");
            }

            var credentials = CredentialsFactory.GetCredentials(_credentialsType);
            return credentials.Authenticate(_sqlServer, _database, _username, _password, _domain);
        }

        /// <summary>
        /// Duplicates this AuthenticationService with a new connection.
        /// </summary>
        /// <returns>A new AuthenticationService object with the same parameters.</returns>
        public AuthenticationService Duplicate()
        {
            var duplicateService = new AuthenticationService(Server);
            if (!duplicateService.Authenticate(_credentialsType, _sqlServer, _database, _username, _password, _domain))
            {
                throw new InvalidOperationException("Failed to duplicate authentication service.");
            }
            return duplicateService;
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
