using MSSQLand.Utilities;
using System;
using System.Data.SqlClient;
using System.Reflection;

namespace MSSQLand.Services.Credentials
{
    public abstract class BaseCredentials
    {

        private readonly int _connectTimeout = 5;

        /// <summary>
        /// Indicates whether the current authentication attempt was successful.
        /// </summary>
        public bool IsAuthenticated { get; protected set; } = false;

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
            string appName = $"SQL-Prod-V{Assembly.GetExecutingAssembly().GetName().Version}"; 
            string workstationId = $"WS-{Guid.NewGuid().ToString("N").Substring(0, 6)}";

            connectionString = $"{connectionString.TrimEnd(';')}; Connect Timeout={_connectTimeout}; Application Name={appName}; Workstation Id={workstationId}";

            Logger.Task($"Trying to connect with {GetName()}");
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
                Logger.SuccessNested($"Client Connection ID: {connection.ClientConnectionId}");

                return connection;
            }
            catch (SqlException ex)
            {
                Logger.Error($"SQL error while opening connection: {ex.Message}");
                connection.Dispose();
                throw new InvalidOperationException("Failed to open SQL connection. See inner exception for details.", ex);
            }
            catch (InvalidOperationException ex)
            {
                Logger.Error($"Invalid operation while opening connection: {ex.Message}");
                connection.Dispose();
                throw new InvalidOperationException("Invalid operation occurred while opening SQL connection.", ex);
            }
            catch (Exception ex)
            {
                Logger.Error($"Unexpected error while opening connection: {ex.Message}");
                connection.Dispose();
                throw new InvalidOperationException("An unexpected error occurred while opening SQL connection.", ex);
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
