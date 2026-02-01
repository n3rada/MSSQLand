using System;
using System.Collections.Generic;
using System.Linq;
using MSSQLand.Models;

namespace MSSQLand.Services.Credentials
{
    /// <summary>
    /// Metadata for a credential type, including required arguments and description.
    /// </summary>
    public class CredentialMetadata
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public List<string> RequiredArguments { get; set; }
        public List<string> OptionalArguments { get; set; }
        public Func<Server, BaseCredentials> Factory { get; set; }

        public CredentialMetadata(string name, string description, List<string> requiredArguments, Func<Server, BaseCredentials> factory, List<string> optionalArguments = null)
        {
            Name = name;
            Description = description;
            RequiredArguments = requiredArguments ?? new List<string>();
            OptionalArguments = optionalArguments ?? new List<string>();
            Factory = factory;
        }
    }

    public static class CredentialsFactory
    {
        /// <summary>
        /// Registry of all available credential types with their metadata.
        /// </summary>
        private static readonly Dictionary<string, CredentialMetadata> _credentialRegistry = new(StringComparer.OrdinalIgnoreCase)
        {
            {
                "probe",
                new CredentialMetadata(
                    name: "probe",
                    description: "Test if SQL Server is alive (no auth, just connectivity check)",
                    requiredArguments: new List<string>(),
                    factory: ProbeCredentials.Create
                )
            },
            {
                "token",
                new CredentialMetadata(
                    name: "token",
                    description: "Windows Integrated Security (current process token)",
                    requiredArguments: new List<string>(),
                    factory: TokenCredentials.Create
                )
            },
            {
                "domain",
                new CredentialMetadata(
                    name: "domain",
                    description: "Domain account authentication via impersonation",
                    requiredArguments: new List<string> { "username", "password", "domain" },
                    factory: WindowsCredentials.Create
                )
            },
            {
                "windows",
                new CredentialMetadata(
                    name: "windows",
                    description: "Windows Authentication with impersonation (domain or local account)",
                    requiredArguments: new List<string> { "username", "password" },
                    factory: WindowsCredentials.Create,
                    optionalArguments: new List<string> { "domain" }
                )
            },
            {
                "local",
                new CredentialMetadata(
                    name: "local",
                    description: "SQL Server authentication (on-premises and Azure SQL)",
                    requiredArguments: new List<string> { "username", "password" },
                    factory: LocalCredentials.Create
                )
            },
            {
                "entraid",
                new CredentialMetadata(
                    name: "entraid",
                    description: "Azure Active Directory authentication (Azure SQL only)",
                    requiredArguments: new List<string> { "username", "password", "domain" },
                    factory: EntraIDCredentials.Create
                )
            }
        };

        /// <summary>
        /// Gets all available credential types with their metadata.
        /// </summary>
        /// <returns>Dictionary of credential type name to metadata.</returns>
        public static IReadOnlyDictionary<string, CredentialMetadata> GetAvailableCredentials()
        {
            return _credentialRegistry;
        }

        /// <summary>
        /// Gets the metadata for a specific credential type.
        /// </summary>
        /// <param name="credentialsType">The credential type name.</param>
        /// <returns>Credential metadata.</returns>
        /// <exception cref="ArgumentException">If the credential type is not supported.</exception>
        public static CredentialMetadata GetCredentialMetadata(string credentialsType)
        {
            if (string.IsNullOrWhiteSpace(credentialsType))
            {
                throw new ArgumentException("Credential type cannot be null or empty.", nameof(credentialsType));
            }

            if (!_credentialRegistry.TryGetValue(credentialsType, out var metadata))
            {
                var availableTypes = string.Join(", ", _credentialRegistry.Keys);
                throw new ArgumentException(
                    $"Unsupported credentials type: '{credentialsType}'. Available types: {availableTypes}",
                    nameof(credentialsType)
                );
            }

            return metadata;
        }

        /// <summary>
        /// Checks if a credential type exists.
        /// </summary>
        /// <param name="credentialsType">The credential type name.</param>
        /// <returns>True if the credential type is supported, false otherwise.</returns>
        public static bool IsValidCredentialType(string credentialsType)
        {
            return !string.IsNullOrWhiteSpace(credentialsType) &&
                   _credentialRegistry.ContainsKey(credentialsType);
        }

        /// <summary>
        /// Gets a credentials instance for the specified type.
        /// </summary>
        /// <param name="credentialsType">The type of credentials to create.</param>
        /// <param name="server">The target Server for this credential instance.</param>
        /// <returns>An instance of the appropriate credentials class.</returns>
        /// <exception cref="ArgumentException">If the credential type is not supported.</exception>
        public static BaseCredentials GetCredentials(string credentialsType, Server server)
        {
            if (server == null)
                throw new ArgumentNullException(nameof(server));
            
            var metadata = GetCredentialMetadata(credentialsType);
            return metadata.Factory(server);
        }

        /// <summary>
        /// Gets all credential type names.
        /// </summary>
        /// <returns>List of credential type names.</returns>
        public static List<string> GetCredentialTypeNames()
        {
            return _credentialRegistry.Keys.ToList();
        }
    }
}
