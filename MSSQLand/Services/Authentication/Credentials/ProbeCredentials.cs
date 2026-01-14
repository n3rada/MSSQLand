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
            // Reasonable timeout for probing - allows for network latency
            SetConnectionTimeout(10);
        }

        public override SqlConnection Authenticate(string sqlServer, string database, string username = null, string password = null, string domain = null)
        {
            Logger.Info($"Probing SQL Server: {sqlServer}");
            Logger.InfoNested("Using empty credentials to test if server is alive");

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
                }
                // Error -2, -1, 258 = Timeout / connection failed
                else if (ex.Number == -2 || ex.Number == -1 || ex.Number == 258)
                {
                    Logger.Error("Connection timeout");
                    Logger.ErrorNested("Server did not respond. May be offline, blocked, or not a SQL Server");
                }
                // Error 53 = Network path not found
                else if (ex.Number == 53)
                {
                    Logger.Error("Network path not found");
                    Logger.ErrorNested("Server unreachable. Check hostname/IP and network connectivity");
                }
                // Other SQL errors still indicate the server responded
                else
                {
                    Logger.Success("SQL Server responded");
                    Logger.InfoNested($"Error {ex.Number}: {ex.Message}");
                }

                // Don't re-throw - probe is complete, we've logged the result
                // Return null to indicate no connection was established
                return null;
            }
        }
    }
}
