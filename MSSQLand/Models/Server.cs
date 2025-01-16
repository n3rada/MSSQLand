

using MSSQLand.Services;
using MSSQLand.Utilities;

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

        public int Port { get; set; } = 1433; // Default SQL Server port

        public string Database { get; set; } = "master";

        /// <summary>
        /// The user to impersonate on this server (optional).
        /// </summary>
        public string ImpersonationUser { get; set; }


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

    }
}
