using MSSQLand.Utilities;
using System.Data.SqlClient;

namespace MSSQLand.Services.Credentials
{
    /// <summary>
    /// Probe credentials for testing SQL Server connectivity without valid authentication.
    /// Uses SQL authentication with empty credentials - server will reject but confirms it's responding.
    /// This is useful for checking if a SQL Server is alive before sending real credentials.
    /// Tries TCP first, then Named Pipes as fallback - any response proves server is alive.
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
            // Extract hostname for Named Pipes fallback
            string hostname = ExtractHostname(sqlServer);
            
            // Try TCP first (more common, faster)
            Logger.Info($"Probing SQL Server via TCP: {sqlServer}");
            Logger.InfoNested("Using empty credentials to test if server is alive");
            Logger.NewLine();

            var tcpResult = TryProbe($"Server={sqlServer}; Database=master; Integrated Security=False; User Id=; Password=;");
            
            if (tcpResult != ProbeResult.Timeout)
            {
                // Got a response (success or error) - server is alive
                return null;
            }

            // TCP timed out - try Named Pipes as fallback
            Logger.NewLine();
            string pipePath = $@"np:\\{hostname}\pipe\sql\query";
            Logger.Info($"TCP timed out, trying Named Pipes: {pipePath}");
            Logger.InfoNested("Named Pipes response would confirm server is alive");
            Logger.NewLine();

            TryProbe($"Server={pipePath}; Database=master; Integrated Security=False; User Id=; Password=;");

            return null;
        }

        private enum ProbeResult
        {
            Alive,      // Server responded (even with auth error)
            Timeout,    // No response
            Unreachable // Network error
        }

        private ProbeResult TryProbe(string connectionString)
        {
            try
            {
                CreateSqlConnection(connectionString);
                return ProbeResult.Alive;
            }
            catch (SqlException ex)
            {
                // Error 18456 = Login failed - this means the server IS alive and responding
                if (ex.Number == 18456)
                {
                    Logger.Success("SQL Server is alive and responding");
                    Logger.SuccessNested("Server rejected empty credentials (expected)");
                    return ProbeResult.Alive;
                }
                // Error 1225 = Connection refused - server alive but not listening on this port
                else if (ex.Number == 1225)
                {
                    Logger.Warning("Connection refused on this port");
                    Logger.WarningNested("Server is reachable but not listening on the specified port");
                    Logger.WarningNested("Use -browse to query SQL Browser for available instances");
                    return ProbeResult.Alive;
                }
                // Error -2, -1, 258 = Timeout / connection failed
                else if (ex.Number == -2 || ex.Number == -1 || ex.Number == 258)
                {
                    Logger.Error("Connection timeout");
                    Logger.ErrorNested("Server did not respond. May be offline, blocked, or not a SQL Server");
                    return ProbeResult.Timeout;
                }
                // Error 53 = Network path not found
                else if (ex.Number == 53)
                {
                    Logger.Error("Network path not found");
                    Logger.ErrorNested("Server unreachable. Check hostname/IP and network connectivity");
                    return ProbeResult.Unreachable;
                }
                // Other SQL errors still indicate the server responded
                else
                {
                    Logger.Success("SQL Server responded");
                    Logger.InfoNested($"Error {ex.Number}: {ex.Message}");
                    return ProbeResult.Alive;
                }
            }
        }

        /// <summary>
        /// Extracts hostname from connection target.
        /// Handles: tcp:hostname,port | hostname,port | hostname
        /// </summary>
        private static string ExtractHostname(string sqlServer)
        {
            string s = sqlServer;
            
            // Remove tcp: prefix
            if (s.StartsWith("tcp:", System.StringComparison.OrdinalIgnoreCase))
                s = s.Substring(4);
            
            // Remove port
            int commaIndex = s.IndexOf(',');
            if (commaIndex > 0)
                s = s.Substring(0, commaIndex);
            
            return s;
        }
    }
}
