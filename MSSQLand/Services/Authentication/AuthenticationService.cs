// MSSQLand/Services/Authentication/AuthenticationService.cs

using MSSQLand.Models;
using MSSQLand.Services.Credentials;
using MSSQLand.Exceptions;
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
        private int _connectionTimeout = 5;
        private BaseCredentials _credentials;

        /// <summary>
        /// Gets the credentials type used for authentication.
        /// </summary>
        public string CredentialsType => _credentialsType;

        /// <summary>
        /// Gets the credentials instance used for authentication.
        /// </summary>
        public BaseCredentials Credentials => _credentials;

        public AuthenticationService(Server server)
        {
            Server = server;
        }

        /// <summary>
        /// Authenticates and establishes a connection to the database using the specified credentials type.
        /// Throws AuthenticationFailedException if authentication fails.
        /// </summary>
        /// <param name="credentialsType">The type of credentials to use (e.g., "token", "domain").</param>
        /// <param name="sqlServer">The SQL server address.</param>
        /// <param name="database">The target database.</param>
        /// <param name="username">The username (optional).</param>
        /// <param name="password">The password (optional).</param>
        /// <param name="domain">The domain for Windows authentication (optional).</param>
        /// <param name="connectionTimeout">The connection timeout in seconds (default: 5).</param>
        public void Authenticate(
            string credentialsType,
            string sqlServer,
            string database = "master",
            string username = null,
            string password = null,
            string domain = null,
            int connectionTimeout = 5)
        {
            // Store authentication parameters
            _credentialsType = credentialsType;
            _sqlServer = sqlServer;
            _database = database;
            _username = username;
            _password = password;
            _domain = domain;
            _connectionTimeout = connectionTimeout;

            // Get the appropriate credentials service
            _credentials = CredentialsFactory.GetCredentials(credentialsType);
            _credentials.SetConnectionTimeout(connectionTimeout);

            // Use the credentials service to authenticate and establish the connection
            Connection = _credentials.Authenticate(sqlServer, database, username, password, domain);

            if (Connection == null)
            {
                throw new AuthenticationFailedException(sqlServer, credentialsType);
            }

            Server.Version = Connection.ServerVersion;
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
            credentials.SetConnectionTimeout(_connectionTimeout);
            return credentials.Authenticate(_sqlServer, _database, _username, _password, _domain);
        }

        /// <summary>
        /// Duplicates this AuthenticationService with a new connection.
        /// Throws AuthenticationFailedException if duplication fails.
        /// </summary>
        /// <returns>A new AuthenticationService object with the same parameters.</returns>
        public AuthenticationService Duplicate()
        {
            var duplicateService = new AuthenticationService(Server);
            duplicateService.Authenticate(_credentialsType, _sqlServer, _database, _username, _password, _domain, _connectionTimeout);
            return duplicateService;
        }

        public void Dispose()
        {
            if (Connection != null)
            {
                // Just dispose - it automatically closes the connection
                Connection.Dispose();
                Connection = null;
            }
        }
    }
}
