using System;

namespace MSSQLand.Services.Credentials
{
    public static class CredentialsFactory
    {
        public static BaseCredentials GetCredentials(string credentialsType)
        {
            return credentialsType.ToLower() switch
            {
                "token" => new TokenCredentials(),
                "domain" => new DomainCredentials(),
                "local" => new LocalCredentials(),
                "entraid" => new EntraIDCredentials(),
                "azure" => new AzureCredentials(),
                _ => throw new ArgumentException($"Unsupported credentials type: {credentialsType}")
            };
        }
    }
}
