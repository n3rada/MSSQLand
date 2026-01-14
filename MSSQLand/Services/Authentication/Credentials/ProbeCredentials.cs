using MSSQLand.Utilities;
using System.Data.SqlClient;

namespace MSSQLand.Services.Credentials
{
    /// <summary>
    /// Probe credentials for testing SQL Server connectivity without valid authentication.
    /// Uses SQL authentication with empty credentials - server will reject but confirms it's responding.
    /// This is useful for checking if a SQL Server is alive before sending real credentials.
    /// </summary>
    public class ProbeCredentials : BaseCredentials
    {
        public ProbeCredentials()
        {
            // Short timeout for probing - we just want to know if it's alive
            SetConnectionTimeout(5);
        }

        public override SqlConnection Authenticate(string sqlServer, string database, string username = null, string password = null, string domain = null)
        {
            Logger.TaskNested($"Probing SQL Server: {sqlServer}");
            Logger.TaskNested("Using empty credentials to test if server is alive");

            // Use SQL auth with empty credentials
            // Server will reject with error 18456 (login failed) = server is alive
            var connectionString = $"Server={sqlServer}; Database=master; Integrated Security=False; User Id=; Password=;";
            
            try
            {
                return CreateSqlConnection(connectionString);
            }
            catch (SqlException ex)
            {
                // Error 18456 = Login failed - this means the server IS alive and responding
                if (ex.Number == 18456)
                {
                    Logger.Success("SQL Server is alive and responding");
                    Logger.SuccessNested("Server rejected empty credentials (expected)");
                    IsAuthenticated = false; // Mark as not authenticated but server is reachable
                }
                // Error -2 or -1 = Timeout / connection failed
                else if (ex.Number == -2 || ex.Number == -1)
                {
                    Logger.Error("Connection timeout");
                    Logger.ErrorNested("Server did not respond - may be offline, blocked, or not a SQL Server");
                }
                // Error 53 = Network path not found
                else if (ex.Number == 53)
                {
                    Logger.Error("Network path not found");
                    Logger.ErrorNested("Server unreachable - check hostname/IP and network connectivity");
                }
                // Other SQL errors still indicate the server responded
                else
                {
                    Logger.Success("SQL Server responded");
                    Logger.InfoNested($"Error {ex.Number}: {ex.Message}");
                }

                // Re-throw to signal probe completed (caller will handle)
                throw;
            }
        }
    }
}
