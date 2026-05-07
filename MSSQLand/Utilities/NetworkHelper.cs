// MSSQLand/Utilities/NetworkHelper.cs

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace MSSQLand.Utilities
{
    internal static class NetworkHelper
    {
        /// <summary>
        /// Parses username for embedded domain (DOMAIN\user or user@domain format).
        /// Returns (username, domain) tuple. Domain is null if not embedded.
        /// </summary>
        public static (string username, string domain) ParseUsernameWithDomain(string input)
        {
            if (string.IsNullOrEmpty(input))
                return (input, null);

            // Check for DOMAIN\username format (NetBIOS style)
            int backslashIndex = input.IndexOf('\\');
            if (backslashIndex > 0 && backslashIndex < input.Length - 1)
            {
                string domain = input.Substring(0, backslashIndex);
                string username = input.Substring(backslashIndex + 1);
                return (username, domain);
            }

            // Check for username@domain format (UPN style)
            // Only treat as UPN if @ is not at the start and there's content after @
            int atIndex = input.IndexOf('@');
            if (atIndex > 0 && atIndex < input.Length - 1)
            {
                string username = input.Substring(0, atIndex);
                string domain = input.Substring(atIndex + 1);
                return (username, domain);
            }

            return (input, null);
        }

        /// <summary>
        /// Validates DNS resolution for a hostname and returns resolved IP addresses.
        /// Skips validation for localhost-like addresses and named pipes.
        /// </summary>
        /// <param name="hostname">The hostname to resolve.</param>
        /// <param name="throwOnFailure">If true, throws ArgumentException on failure. If false, returns null.</param>
        /// <returns>Array of resolved IP addresses, or null if resolution fails and throwOnFailure is false.</returns>
        /// <exception cref="ArgumentException">Thrown when DNS resolution fails and throwOnFailure is true.</exception>
        public static IPAddress[] ValidateDnsResolution(string hostname, bool throwOnFailure = true)
        {
            if (string.IsNullOrWhiteSpace(hostname))
            {
                if (throwOnFailure)
                    throw new ArgumentException("Hostname cannot be null or empty.");
                return null;
            }

            // Skip validation for localhost-like addresses
            string lower = hostname.ToLowerInvariant();
            if (lower == "localhost" ||
                lower == "127.0.0.1" ||
                lower == "::1" ||
                lower == "(local)" ||
                lower == ".")
            {
                Logger.Debug($"Skipping DNS resolution for localhost address: {hostname}");
                return null;
            }

            // Try to parse as IP address first
            if (IPAddress.TryParse(hostname, out IPAddress ipAddress))
            {
                Logger.Debug($"Hostname is already an IP address: {hostname}");
                return new[] { ipAddress };
            }

            // Perform DNS resolution
            try
            {
                Logger.Debug($"Resolving hostname: {hostname}");
                IPAddress[] addresses = Dns.GetHostAddresses(hostname);
                if (addresses.Length > 0)
                {
                    Logger.DebugNested($"Resolved to: {string.Join(", ", addresses.Select(a => a.ToString()))}");
                    return addresses;
                }
                else
                {
                    if (throwOnFailure)
                        throw new ArgumentException($"DNS resolution for '{hostname}' returned no addresses.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                if (throwOnFailure)
                    throw new ArgumentException($"DNS resolution failed for '{hostname}': {ex.Message}. Verify the hostname and network connectivity.");
                return null;
            }
        }

        /// <summary>
        /// Gets a random available TCP port on the loopback interface.
        /// Binds a socket to port 0 which causes the OS to assign an available ephemeral port.
        /// </summary>
        /// <returns>An available TCP port number.</returns>
        public static int GetRandomUnusedPort()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)socket.LocalEndPoint).Port;
        }
    }
}
