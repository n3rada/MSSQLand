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
        private string _appName;
        private string _workstationId;
        private int? _packetSize;
        private bool? _enableEncryption;
        private bool? _trustServerCertificate;
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
        /// <param name="appName">Custom application name (optional).</param>
        /// <param name="workstationId">Custom workstation ID (optional).</param>
        /// <param name="packetSize">Network packet size in bytes (optional).</param>
        /// <param name="enableEncryption">Override encryption setting (optional).</param>
        /// <param name="trustServerCertificate">Override server certificate trust (optional).</param>
        public void Authenticate(
            string credentialsType,
            string sqlServer,
            string database = "master",
            string username = null,
            string password = null,
            string domain = null,
            int connectionTimeout = 5,
            string appName = null,
            string workstationId = null,
            int? packetSize = null,
            bool? enableEncryption = null,
            bool? trustServerCertificate = null)
        {
            // Store authentication parameters
            _credentialsType = credentialsType;
            _sqlServer = sqlServer;
            _database = database;
            _username = username;
            _password = password;
            _domain = domain;
            _connectionTimeout = connectionTimeout;
            _appName = appName;
            _workstationId = workstationId;
            _packetSize = packetSize;
            _enableEncryption = enableEncryption;
            _trustServerCertificate = trustServerCertificate;

            // Get the appropriate credentials service
            _credentials = CredentialsFactory.GetCredentials(credentialsType);
            _credentials.SetConnectionTimeout(connectionTimeout);
            
            // Apply connection string customization if provided
            if (!string.IsNullOrEmpty(appName))
                _credentials.AppName = appName;
            if (!string.IsNullOrEmpty(workstationId))
                _credentials.WorkstationId = workstationId;
            if (packetSize.HasValue)
                _credentials.PacketSize = packetSize;
            
            // Apply connection string boolean overrides if provided
            if (enableEncryption.HasValue)
                _credentials.EnableEncryption = enableEncryption;
            if (trustServerCertificate.HasValue)
                _credentials.TrustServerCertificate = trustServerCertificate;

            // Use the credentials service to authenticate and establish the connection
            Connection = _credentials.Authenticate(sqlServer, database, username, password, domain);

            if (Connection == null)
            {
                throw new AuthenticationFailedException(sqlServer, credentialsType);
            }

            Server.Version = Connection.ServerVersion;
            
            // Set database from connection if not already set
            if (string.IsNullOrEmpty(Server.Database))
            {
                Server.Database = Connection.Database;
            }
            
            // Query actual SQL Server name and set Server.Hostname (includes instance name)
            try
            {
                using (SqlCommand cmd = new SqlCommand("SELECT @@SERVERNAME", Connection))
                {
                    string serverName = cmd.ExecuteScalar()?.ToString();
                    if (!string.IsNullOrEmpty(serverName))
                    {
                        Server.Hostname = serverName;
                    }
                }
            }
            catch
            {
                // Keep the original hostname if query fails
            }
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
            
            // Apply connection string customization if provided
            if (!string.IsNullOrEmpty(_appName))
                credentials.AppName = _appName;
            if (!string.IsNullOrEmpty(_workstationId))
                credentials.WorkstationId = _workstationId;
            if (_packetSize.HasValue)
                credentials.PacketSize = _packetSize;
            
            // Apply connection string boolean overrides if provided
            if (_enableEncryption.HasValue)
                credentials.EnableEncryption = _enableEncryption;
            if (_trustServerCertificate.HasValue)
                credentials.TrustServerCertificate = _trustServerCertificate;
            
            return credentials.Authenticate(_sqlServer, _database, _username, _password, _domain);
        }

        /// <summary>
        /// Duplicates this AuthenticationService with a new connection.
        /// Throws AuthenticationFailedException if duplication fails.
        /// </summary>
        /// <returns>A new AuthenticationService object with the same parameters.</returns>
        public AuthenticationService Duplicate()
        {
            // Create a copy of the Server to avoid shared state
            var duplicateService = new AuthenticationService(Server.Copy());
            duplicateService.Authenticate(_credentialsType, _sqlServer, _database, _username, _password, _domain, _connectionTimeout, _appName, _workstationId, _packetSize, _enableEncryption, _trustServerCertificate);
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
