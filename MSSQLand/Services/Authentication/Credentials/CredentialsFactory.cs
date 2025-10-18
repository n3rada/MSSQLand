using System;
using System.Collections.Generic;
using System.Linq;

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
        public Func<BaseCredentials> Factory { get; set; }

        public CredentialMetadata(string name, string description, List<string> requiredArguments, Func<BaseCredentials> factory)
        {
            Name = name;
            Description = description;
            RequiredArguments = requiredArguments ?? new List<string>();
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
                "token",
                new CredentialMetadata(
                    name: "token",
                    description: "Windows Integrated Security using current process token (Kerberos/NTLM)",
                    requiredArguments: new List<string>(),
                    factory: () => new TokenCredentials()
                )
            },
            {
                "domain",
                new CredentialMetadata(
                    name: "domain",
                    description: "Windows Authentication with explicit credentials (using impersonation)",
                    requiredArguments: new List<string> { "username", "password", "domain" },
                    factory: () => new DomainCredentials()
                )
            },
            {
                "local",
                new CredentialMetadata(
                    name: "local",
                    description: "SQL Server local authentication (SQL user/password)",
                    requiredArguments: new List<string> { "username", "password" },
                    factory: () => new LocalCredentials()
                )
            },
            {
                "entraid",
                new CredentialMetadata(
                    name: "entraid",
                    description: "Entra ID (Azure Active Directory) authentication",
                    requiredArguments: new List<string> { "username", "password" },
                    factory: () => new EntraIDCredentials()
                )
            },
            {
                "azure",
                new CredentialMetadata(
                    name: "azure",
                    description: "Azure SQL Database authentication",
                    requiredArguments: new List<string> { "username", "password" },
                    factory: () => new AzureCredentials()
                )
            },
            {
                "windows",
                new CredentialMetadata(
                    name: "windows",
                    description: "Windows Authentication via TDS protocol (NTLM)",
                    requiredArguments: new List<string> { "username", "password", "domain" },
                    factory: () => new WindowsAuthCredentials()
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
        /// <returns>An instance of the appropriate credentials class.</returns>
        /// <exception cref="ArgumentException">If the credential type is not supported.</exception>
        public static BaseCredentials GetCredentials(string credentialsType)
        {
            var metadata = GetCredentialMetadata(credentialsType);
            return metadata.Factory();
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
