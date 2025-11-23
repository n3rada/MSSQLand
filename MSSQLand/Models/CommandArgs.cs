using MSSQLand.Actions;

namespace MSSQLand.Models
{
    /// <summary>
    /// Represents parsed command-line arguments for MSSQLand.
    /// </summary>
    public class CommandArgs
    {
        /// <summary>
        /// The type of credentials provided (e.g., token, domain, local, entraid, azure).
        /// </summary>
        public string CredentialType { get; set; }

        /// <summary>
        /// The username for authentication (if applicable).
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// The password for authentication (if applicable).
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// The domain for domain-based authentication (if applicable).
        /// </summary>
        public string Domain { get; set; }

        /// <summary>
        /// The primary target server to interact with.
        /// </summary>
        public Server Host { get; set; }

        /// <summary>
        /// A list of linked servers in the server chain.
        /// </summary>
        public LinkedServers LinkedServers { get; set; }

        /// <summary>
        /// The action to execute.
        /// </summary>
        public BaseAction Action { get; set; }

        /// <summary>
        /// The connection timeout in seconds (default: 15).
        /// </summary>
        public int ConnectionTimeout { get; set; } = 15;

    }
}
