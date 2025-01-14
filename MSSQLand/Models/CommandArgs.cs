using MSSQLand.Actions;
using System.Collections.Generic;

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
        public Server Target { get; set; }

        /// <summary>
        /// A list of linked servers in the server chain.
        /// </summary>
        public LinkedServers LinkedServers { get; set; }


        public BaseAction Action { get; set; }

        public string AdditionalArguments { get; set; }

        /// <summary>
        /// The query string to execute (optional, for actions like "query").
        /// </summary>
        public string Query { get; set; }

    }
}
