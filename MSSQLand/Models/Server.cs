// MSSQLand/Models/Server.cs

using MSSQLand.Utilities;
using System;

namespace MSSQLand.Models
{
    /// <summary>
    /// Represents a SQL Server connection configuration.
    /// This class contains static connection information (hostname, port, database, etc.)
    /// For runtime execution state (users, privileges), see ServerExecutionState.
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
                    IsLegacy = true;
                }
            }
        }

        /// <summary>
        /// The major version of the server (e.g., 15 for "15.00.2000").
        /// </summary>
        public int MajorVersion { get; private set; }

        public bool IsLegacy { get; private set; } = false;

        public bool IsAzureSQL { get; set; } = false;

        public int Port { get; set; } = 1433; // Default SQL Server port

        public string Database { get; set; } = null;

        /// <summary>
        /// The user to impersonate on this server (optional).
        /// </summary>
        public string ImpersonationUser { get; set; }

        /// <summary>
        /// The mapped database user (runtime state, populated by UserService).
        /// </summary>
        public string MappedUser { get; set; }
        
        /// <summary>
        /// The system login user (runtime state, populated by UserService).
        /// </summary>
        public string SystemUser { get; set; }

        /// <summary>
        /// Creates a copy of this Server instance.
        /// </summary>
        public Server Copy()
        {
            return new Server
            {
                Hostname = this.Hostname,
                version = this.version,
                MajorVersion = this.MajorVersion,
                IsLegacy = this.IsLegacy,
                IsAzureSQL = this.IsAzureSQL,
                Port = this.Port,
                Database = this.Database,
                ImpersonationUser = this.ImpersonationUser,
                MappedUser = this.MappedUser,
                SystemUser = this.SystemUser
            };
        }

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
        /// Format: server[:port][/user][@database] or [server][:port][/user][@database]
        /// Components are parsed in order from left to right:
        /// - hostname (required): everything before the first delimiter, or content in brackets
        /// - :port (optional): port number after colon
        /// - /user (optional): impersonation user after forward slash
        /// - @database (optional): database context after at sign
        /// Brackets [...] protect hostnames containing delimiters from being split
        /// Examples:
        /// - server
        /// - server:1434
        /// - server/user
        /// - server@database
        /// - server:1434/user@database
        /// - server/user@database
        /// - server:1434@database
        /// - [SQL02;PROD]:1433
        /// - [SERVER@INSTANCE]/user@database
        /// </summary>
        /// <param name="serverInput">The server input string to parse.</param>
        public static Server ParseServer(string serverInput)
        {
            if (string.IsNullOrWhiteSpace(serverInput))
                throw new ArgumentException("Server input cannot be null or empty.");

            Server server = new();
            string remaining = serverInput;
            char firstDelimiter = '\0';

            // Check if hostname is bracketed (for hostnames containing delimiters)
            if (remaining.StartsWith("["))
            {
                int closingBracket = remaining.IndexOf(']');
                if (closingBracket == -1)
                    throw new ArgumentException($"Unclosed bracket in server specification: {serverInput}");

                // Extract hostname without brackets
                server.Hostname = remaining.Substring(1, closingBracket - 1);
                
                if (string.IsNullOrWhiteSpace(server.Hostname))
                    throw new ArgumentException("Server hostname cannot be empty");

                // Continue parsing modifiers after the closing bracket
                remaining = remaining.Substring(closingBracket + 1);
                
                // If nothing remains after bracket, we're done
                if (string.IsNullOrWhiteSpace(remaining))
                    return server;
                
                // Determine first modifier delimiter after bracket
                if (remaining.Length > 0)
                {
                    if (remaining[0] == ':')
                        firstDelimiter = ':';
                    else if (remaining[0] == '/')
                        firstDelimiter = '/';
                    else if (remaining[0] == '@')
                        firstDelimiter = '@';
                    else
                        throw new ArgumentException($"Invalid character after closing bracket: {remaining[0]}");
                    
                    remaining = remaining.Substring(1);
                }
            }
            else
            {
                // Extract hostname (everything before the first delimiter)
                int firstDelimiterIndex = remaining.Length;

                int colonIndex = remaining.IndexOf(':');
                int slashIndex = remaining.IndexOf('/');
                int atIndex = remaining.IndexOf('@');

                if (colonIndex >= 0 && colonIndex < firstDelimiterIndex) { firstDelimiterIndex = colonIndex; firstDelimiter = ':'; }
                if (slashIndex >= 0 && slashIndex < firstDelimiterIndex) { firstDelimiterIndex = slashIndex; firstDelimiter = '/'; }
                if (atIndex >= 0 && atIndex < firstDelimiterIndex) { firstDelimiterIndex = atIndex; firstDelimiter = '@'; }

                if (firstDelimiterIndex == remaining.Length)
                {
                    // No delimiters, just hostname
                    if (string.IsNullOrWhiteSpace(remaining))
                        throw new ArgumentException("Server hostname cannot be empty");
                    server.Hostname = remaining;
                    return server;
                }

                server.Hostname = remaining.Substring(0, firstDelimiterIndex);
                if (string.IsNullOrWhiteSpace(server.Hostname))
                    throw new ArgumentException("Server hostname cannot be empty");

                remaining = remaining.Substring(firstDelimiterIndex + 1);
            }

            // Extract all components from remaining string
            while (!string.IsNullOrWhiteSpace(remaining))
            {
                // Find next delimiter and what it is
                int colonIndex = remaining.IndexOf(':');
                int slashIndex = remaining.IndexOf('/');
                int atIndex = remaining.IndexOf('@');

                int nextDelimiterIndex = remaining.Length;
                char nextDelimiter = '\0';

                if (colonIndex >= 0 && colonIndex < nextDelimiterIndex) { nextDelimiterIndex = colonIndex; nextDelimiter = ':'; }
                if (slashIndex >= 0 && slashIndex < nextDelimiterIndex) { nextDelimiterIndex = slashIndex; nextDelimiter = '/'; }
                if (atIndex >= 0 && atIndex < nextDelimiterIndex) { nextDelimiterIndex = atIndex; nextDelimiter = '@'; }

                string component = remaining.Substring(0, nextDelimiterIndex);

                if (string.IsNullOrWhiteSpace(component))
                {
                    if (firstDelimiter == ':')
                        throw new ArgumentException("Port cannot be empty after ");
                    else if (firstDelimiter == '/')
                        throw new ArgumentException("Impersonation user cannot be empty after /");
                    else
                        throw new ArgumentException("Database cannot be empty after @");
                }

                // Determine what component this is based on what delimiter preceded it
                if (firstDelimiter == ':')
                {
                    if (!int.TryParse(component, out int port) || port <= 0 || port > 65535)
                        throw new ArgumentException($"Invalid port number: {component}. Port must be between 1 and 65535.");
                    server.Port = port;
                }
                else if (firstDelimiter == '/')
                {
                    server.ImpersonationUser = component;
                }
                else if (firstDelimiter == '@')
                {
                    server.Database = component;
                }

                if (nextDelimiterIndex == remaining.Length)
                    break;

                firstDelimiter = nextDelimiter;
                remaining = remaining.Substring(nextDelimiterIndex + 1);
            }

            return server;
        }
    }
}
