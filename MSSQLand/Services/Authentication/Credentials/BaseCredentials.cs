using MSSQLand.Utilities;
using System;
using System.Data.SqlClient;
using System.Net;
using System.Net.NetworkInformation;

namespace MSSQLand.Services.Credentials
{
    public abstract class BaseCredentials
    {

        private int _connectTimeout = 5;

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
            
            connectionString = $"{connectionString} Connect Timeout={_connectTimeout}";

            Logger.Task("Trying to connect");
            Logger.DebugNested(connectionString);

            var connection = new SqlConnection(connectionString);

            try
            {
                connection.Open();

                Logger.Success($"Connection opened successfully");
                Logger.SuccessNested($"Workstation ID: {connection.WorkstationId}");
                Logger.SuccessNested($"Server Version: {connection.ServerVersion}");
                Logger.SuccessNested($"Database: {connection.Database}");
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

    }
}
