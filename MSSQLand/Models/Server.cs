

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
        
        /// <summary>
        /// Parses a server input string into a Server object.
        /// Format: server[,port][:user][@database]
        /// Order is flexible - server is always first, port after comma, user after colon, database after @.
        /// Examples:
        /// - server
        /// - server,1434
        /// - server:user
        /// - server,1434:user
        /// - server@database
        /// - server,1434@database
        /// - server:user@database
        /// - server,1434:user@database
        /// </summary>
        public static Server ParseServer(string serverInput)
        {
            if (string.IsNullOrWhiteSpace(serverInput))
                throw new ArgumentException("Server input cannot be null or empty.");

            Server server = new();

            // Extract database (everything after @, rightmost @)
            if (serverInput.Contains("@"))
            {
                int lastAtIndex = serverInput.LastIndexOf('@');
                string database = serverInput.Substring(lastAtIndex + 1);
                if (string.IsNullOrWhiteSpace(database))
                    throw new ArgumentException("Database cannot be empty after @");
                server.Database = database;
                serverInput = serverInput.Substring(0, lastAtIndex);
            }

            // Extract impersonation user (everything after :, rightmost :)
            if (serverInput.Contains(":"))
            {
                int lastColonIndex = serverInput.LastIndexOf(':');
                string user = serverInput.Substring(lastColonIndex + 1);
                if (string.IsNullOrWhiteSpace(user))
                    throw new ArgumentException("Impersonation user cannot be empty after :");
                server.ImpersonationUser = user;
                serverInput = serverInput.Substring(0, lastColonIndex);
            }

            // Extract port (everything after ,)
            if (serverInput.Contains(","))
            {
                int commaIndex = serverInput.IndexOf(',');
                string hostname = serverInput.Substring(0, commaIndex);
                string portString = serverInput.Substring(commaIndex + 1);
                
                if (string.IsNullOrWhiteSpace(hostname))
                    throw new ArgumentException("Server hostname cannot be empty");
                if (string.IsNullOrWhiteSpace(portString))
                    throw new ArgumentException("Port cannot be empty after ,");
                    
                if (!int.TryParse(portString, out int port) || port <= 0 || port > 65535)
                    throw new ArgumentException($"Invalid port number: {portString}. Port must be between 1 and 65535.");
                
                server.Hostname = hostname;
                server.Port = port;
            }
            else
            {
                // Just hostname
                if (string.IsNullOrWhiteSpace(serverInput))
                    throw new ArgumentException("Server hostname cannot be empty");
                server.Hostname = serverInput;
            }

            return server;
        }

    }
}
