

using MSSQLand.Utilities;
using System;

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

        private string version;

        /// <summary>
        /// The full version string of the server (e.g., "15.00.2000").
        /// Setting this also updates the major version.
        /// </summary>
        public string Version
        {
            get => version;
            set
            {
                version = value;
                MajorVersion = ParseMajorVersion(version);
                if (MajorVersion <= 13)
                {
                    Logger.Warning("Legacy server");
                    Legacy = true;
                }
            }
        }

        /// <summary>
        /// The major version of the server (e.g., 15 for "15.00.2000").
        /// </summary>
        public int MajorVersion { get; private set; }

        public bool Legacy { get; private set; } = false;

        public bool IsAzureSQL { get; set; } = false;

        public int Port { get; set; } = 1433; // Default SQL Server port

        public string Database { get; set; } = "master";

        /// <summary>
        /// The user to impersonate on this server (optional).
        /// </summary>
        public string ImpersonationUser { get; set; }

        public string MappedUser { get; set; }
        public string SystemUser { get; set; }

        /// <summary>
        /// Parses the major version from the full version string.
        /// </summary>
        /// <param name="versionString">The full version string (e.g., "15.00.2000").</param>
        /// <returns>The major version number.</returns>
        private static int ParseMajorVersion(string versionString)
        {
            if (string.IsNullOrWhiteSpace(versionString)) return 0;

            var versionParts = versionString.Split('.');

            return int.TryParse(versionParts[0], out int majorVersion) ? majorVersion : 0;
        }

        public static Server ParseServer(string serverInput)
        {
            if (string.IsNullOrWhiteSpace(serverInput))
                throw new ArgumentException("Server input cannot be null or empty.");

            // Split by colon to separate server from user@database
            var parts = serverInput.Split(':');

            if (parts.Length < 1 || parts.Length > 2)
                throw new ArgumentException($"Invalid target format: {serverInput}");

            Server server = new();

            // Parse server part (may contain @database)
            string serverPart = parts[0];
            if (serverPart.Contains("@"))
            {
                var serverDbParts = serverPart.Split('@');
                if (serverDbParts.Length != 2 || string.IsNullOrWhiteSpace(serverDbParts[0]))
                    throw new ArgumentException($"Invalid server@database format: {serverPart}");
                
                server.Hostname = serverDbParts[0];
                server.Database = serverDbParts[1];
            }
            else
            {
                server.Hostname = serverPart;
            }

            // Parse user@database part (if present after colon)
            if (parts.Length > 1)
            {
                string userPart = parts[1];
                if (userPart.Contains("@"))
                {
                    var userDbParts = userPart.Split('@');
                    if (userDbParts.Length != 2 || string.IsNullOrWhiteSpace(userDbParts[0]))
                        throw new ArgumentException($"Invalid user@database format: {userPart}");
                    
                    server.ImpersonationUser = userDbParts[0];
                    server.Database = userDbParts[1];
                }
                else
                {
                    server.ImpersonationUser = userPart;
                }
            }

            return server;
        }

    }
}
