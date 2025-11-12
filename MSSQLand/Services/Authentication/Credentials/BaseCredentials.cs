using MSSQLand.Utilities;
using System;
using System.Data.SqlClient;

namespace MSSQLand.Services.Credentials
{
    public abstract class BaseCredentials
    {

        private int _connectTimeout = 15;

        /// <summary>
        /// Indicates whether the current authentication attempt was successful.
        /// </summary>
        public bool IsAuthenticated { get; protected set; } = false;

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
        public abstract SqlConnection Authenticate(string sqlServer, string database, string username = null, string password = null, string domain = null);

        /// <summary>
        /// Creates and opens a SQL connection with a specified connection string.
        /// </summary>
        /// <param name="connectionString">The SQL connection string.</param>
        /// <returns>An open <see cref="SqlConnection"/> object.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the connection could not be opened.</exception>
        /// <exception cref="SqlException">Thrown for SQL-related issues (e.g., network errors, authentication issues).</exception>
        protected SqlConnection CreateSqlConnection(string connectionString)
        {
            // Avoid leaving the default ".Net SqlClient Data Provider" application name
            string appName = "DataFactory";
            // Generate a random workstation ID  
            string workstationId = "datafactory-run" + Misc.GetRandomNumber(0, 10);

            connectionString = $"{connectionString.TrimEnd(';')}; Connect Timeout={_connectTimeout}; Application Name={appName}; Workstation Id={workstationId}";

            Logger.Task($"Trying to connect with {GetName()}");
            Logger.TaskNested($"Connection timeout: {_connectTimeout} seconds");
            Logger.DebugNested(connectionString);

            SqlConnection connection = new(connectionString);

            try
            {
                connection.Open();

                Logger.Success($"Connection opened successfully");
                Logger.SuccessNested($"Server: {connection.DataSource}");
                Logger.SuccessNested($"Database: {connection.Database}");
                Logger.SuccessNested($"Server Version: {connection.ServerVersion}");
                Logger.SuccessNested($"Client Workstation ID: {connection.WorkstationId}");
                Logger.SuccessNested($"Client Application Name: {appName}");
                Logger.SuccessNested($"Client Connection ID: {connection.ClientConnectionId}");

                // Note: client_interface_name in sys.dm_exec_sessions is determined by the client library
                // used during the TDS handshake and cannot be changed after connection is established.
                // To change it, you would need to use a different client library (ODBC, OLEDB, JDBC)
                // or intercept/modify the TDS protocol packets before they reach SQL Server.

                return connection;
            }
            catch (SqlException ex)
            {
                Logger.Error($"SQL error while opening connection: {ex.Message}");
                connection.Dispose();
                return null;
            }
            catch (InvalidOperationException ex)
            {
                Logger.Error($"Invalid operation while opening connection: {ex.Message}");
                connection.Dispose();
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Unexpected error while opening connection: {ex.Message}");
                connection.Dispose();
                return null;
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

    }
}
