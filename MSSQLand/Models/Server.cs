

namespace MSSQLand.Models
{
    /// <summary>
    /// Represents a SQL Server with optional impersonation user.
    /// </summary>
    public class Server
    {
        /// <summary>
        /// The hostname or IP address of the server.
        /// </summary>
        public string Hostname { get; set; }

        public int Port { get; set; } = 1433; // Default SQL Server port

        /// <summary>
        /// The user to impersonate on this server (optional).
        /// </summary>
        public string ImpersonationUser { get; set; }



        /// <summary>
        /// Returns a string representation of the server, including the impersonation user if provided.
        /// </summary>
        public override string ToString()
        {
            var userPart = string.IsNullOrEmpty(ImpersonationUser) ? "" : $" ({ImpersonationUser})";
            return $"{Hostname}:{Port}{userPart}";
        }
    }
}
