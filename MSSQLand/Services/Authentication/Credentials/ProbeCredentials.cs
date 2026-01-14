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
            // Reasonable timeout for probing
            SetConnectionTimeout(5);
        }

        public override SqlConnection Authenticate(string sqlServer, string database, string username = null, string password = null, string domain = null)
        {
            Logger.Info($"Probing SQL Server: {sqlServer}");
            Logger.InfoNested("Using empty credentials to test if server is alive");
            Logger.NewLine();

            // Use SQL auth with empty credentials - avoids sending Kerberos ticket
            // Server will reject with 18456 = alive, or network error = unreachable
            var connectionString = $"Server={sqlServer}; Database=master; Integrated Security=False; User Id=; Password=;";

            try
            {
                CreateSqlConnection(connectionString);
                return null;
            }
            catch (SqlException ex)
            {
                Logger.Info($"Error {ex.Number}: {ex.Message}");
                Logger.NewLine();
                // Error 18456 = Login failed - server IS alive and responding
                if (ex.Number == 18456)
                {
                    Logger.Success("SQL Server is alive and responding");
                    Logger.SuccessNested("Server rejected empty credentials (expected)");
                }
                // Error 64 = Pre-login handshake failed - server IS alive, network/protocol issue
                else if (ex.Number == 64)
                {
                    Logger.Success("Server is alive (responded during pre-login handshake)");
                    Logger.Warning("Connection failed during TDS handshake.");
                }
                // Error 1225 = Connection refused - server alive but not listening
                else if (ex.Number == 1225)
                {
                    Logger.Warning("Connection refused on this port");
                    Logger.WarningNested("Server is reachable but not listening on the specified port");
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

                return null;
            }
        }
    }
}
