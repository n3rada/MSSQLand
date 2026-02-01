// MSSQLand/Services/Authentication/Credentials/BaseCredentials.cs

using MSSQLand.Utilities;
using System;
using System.Data.SqlClient;

namespace MSSQLand.Services.Credentials
{
    public abstract class BaseCredentials
    {
        protected Server Server { get; private set; }

        private int _connectTimeout = 5;

        /// <summary>
        /// Indicates whether the current authentication attempt was successful.
        /// </summary>
        public bool IsAuthenticated { get; protected set; } = false;

        /// <summary>
        /// The application name used for the SQL connection.
        /// </summary>
        public string AppName { get; set; } = "SQLAgent - TSQL JobStep";

        /// <summary>
        /// The workstation ID used for the SQL connection.
        /// </summary>
        public string WorkstationId { get; set; } = null;

        /// <summary>
        /// Optional: Override encryption setting. Null uses the credential-specific default.
        /// Default: true (enabled for security)
        /// </summary>
        public bool? EnableEncryption { get; set; } = true;

        /// <summary>
        /// Default: true (accepts self-signed certificates)
        /// </summary>
        public bool? TrustServerCertificate { get; set; } = true;

        /// <summary>
        /// Network packet size in bytes.
        /// Default: null (uses SQL Server default of 8192 bytes)
        /// Set to 4096 if experiencing "Failed to reserve contiguous memory" errors
        /// </summary>
        public int? PacketSize { get; set; } = null;

        /// <summary>
        /// Initializes BaseCredentials with a target Server.
        /// </summary>
        public BaseCredentials(Server server)
        {
            Server = server ?? throw new ArgumentNullException(nameof(server));
        }

        /// <summary>
        /// Sets the connection timeout in seconds.
        /// </summary>
        public void SetConnectionTimeout(int timeout)
        {
            _connectTimeout = timeout;
        }

        /// <summary>
        /// Abstract method to be implemented by derived classes for unique authentication logic.
        /// </summary>
        public abstract SqlConnection Authenticate(string username = null, string password = null, string domain = null);

        /// <summary>
        /// Creates and opens a SQL connection with a specified connection string.
        /// </summary>
        /// <param name="connectionString">The SQL connection string (without database).</param>
        /// <returns>An open <see cref="SqlConnection"/> object.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the connection could not be opened.</exception>
        /// <exception cref="SqlException">Thrown for SQL-related issues (e.g., network errors, authentication issues).</exception>
        protected SqlConnection CreateSqlConnection(string connectionString)
        {

            // If WorkstationId not explicitly set, use target server name
            string workstationId = WorkstationId ?? ExtractServerName(Server.GetConnectionTarget());

            connectionString = $"{connectionString.TrimEnd(';')}; Connect Timeout={_connectTimeout}; Application Name={AppName}; Workstation Id={workstationId}";

            // Add database if provided
            if (!string.IsNullOrEmpty(Server.Database))
                connectionString += $"; Database={Server.Database}";

            // Apply optional connection string overrides (only when different from ADO.NET defaults)
            // Add Encrypt if explicitly set (default varies by .NET version)
            if (EnableEncryption.HasValue)
                connectionString += $"; Encrypt={EnableEncryption.Value}";
            
            // Add TrustServerCertificate if explicitly set (ADO.NET default is False)
            if (TrustServerCertificate.HasValue)
                connectionString += $"; TrustServerCertificate={TrustServerCertificate.Value}";
            
            // Only add Packet Size if explicitly set (ADO.NET uses 8192 by default)
            if (PacketSize.HasValue)
                connectionString += $"; Packet Size={PacketSize.Value}";

            Logger.Debug($"Attempting connection with {GetName()}");
            Logger.DebugNested($"Connection timeout: {_connectTimeout} seconds");
            Logger.DebugNested(connectionString);

            SqlConnection connection = new(connectionString);

            try
            {
                connection.Open();

                // Note: client_interface_name in sys.dm_exec_sessions is determined by the client library
                // used during the TDS handshake and cannot be changed after connection is established.
                // To change it, you would need to use a different client library (ODBC, OLEDB, JDBC)
                // or intercept/modify the TDS protocol packets before they reach SQL Server.

                return connection;
            }
            catch
            {
                Logger.Trace($"Connection attempt failed, disposing connection.");
                connection.Dispose();
                throw; // Re-throw exception to be handled at higher level
            }
        }


        /// <summary>
        /// Returns the name of the class as a string.
        /// </summary>
        /// <returns>The name of the current class.</returns>
        public string GetName()
        {
            return GetType().Name;
        }


        /// <summary>
        /// Extracts the server name from connection string format.
        /// Handles: SQLSERVER01, SQLSERVER01\INSTANCE, SQLSERVER01,1433, etc.
        /// </summary>
        private string ExtractServerName(string sqlServer)
        {
            if (string.IsNullOrWhiteSpace(sqlServer))
                return "SQLNODE01"; // Fallback
            
            // Remove port if present: "SERVER,1433" -> "SERVER"
            string serverName = sqlServer.Split(',')[0];
            
            // Remove instance name if present: "SERVER\INSTANCE" -> "SERVER"
            serverName = serverName.Split('\\')[0];
            
            // Remove protocol prefix if present: "tcp:SERVER" -> "SERVER"
            if (serverName.Contains(":"))
                serverName = serverName.Split(':')[1];
            
            return serverName.Trim();
        }

    }
}
