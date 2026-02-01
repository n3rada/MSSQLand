// MSSQLand/Services/Authentication/Credentials/ProbeCredentials.cs

using MSSQLand.Models;
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
        public ProbeCredentials(Server server) : base(server) { }

        /// <summary>
        /// Factory method for creating ProbeCredentials instances.
        /// </summary>
        public static ProbeCredentials Create(Server server) => new ProbeCredentials(server);

        public override SqlConnection Authenticate(string username = null, string password = null, string domain = null)
        {
            Logger.Info($"Probing SQL Server: {Server.GetConnectionTarget()}");
            Logger.InfoNested("Using empty credentials to test if server is alive");

            // Use SQL auth with empty credentials - avoids sending Kerberos ticket
            // Server will reject with 18456 = alive, or network error = unreachable
            var connectionString = $"Server={Server.GetConnectionTarget()}; Database=master; Integrated Security=False; User Id=; Password=;";

            try
            {
                CreateSqlConnection(connectionString);
                return null;
            }
            catch (SqlException ex)
            {
                Logger.Trace($"SQL Error {ex.Number}: {ex.Message}");
                // Error 18456 = Login failed - server IS alive and responding
                if (ex.Number == 18456)
                {
                    Logger.Success("SQL Server is alive and responding");
                    Logger.SuccessNested("Server rejected empty credentials (expected)");
                }
                // Error 1225 = Connection refused - server alive but not listening
                else if (ex.Number == 1225)
                {
                    Logger.Warning("Connection refused on this port");
                    Logger.WarningNested("Server is reachable but not listening on the specified port");
                }
                // Error 2 = Named Pipes error / server not found
                else if (ex.Number == 2)
                {
                    Logger.Error("Server not found");
                    Logger.ErrorNested("Could not connect. Server may be offline or not a SQL Server");
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
                // Error 64 = Network error / connection dropped
                else if (ex.Number == 64)
                {
                    Logger.Error("Network error");
                    Logger.ErrorNested("Connection failed. Server may be unreachable or not a SQL Server");
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
