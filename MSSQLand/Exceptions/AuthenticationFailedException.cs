using System;

namespace MSSQLand.Exceptions
{
    /// <summary>
    /// Exception thrown when SQL Server authentication fails.
    /// </summary>
    public class AuthenticationFailedException : Exception
    {
        public string Server { get; }
        public string CredentialType { get; }

        public AuthenticationFailedException(string server, string credentialType) 
            : base($"Authentication to '{server}' failed using {credentialType} credentials.")
        {
            Server = server;
            CredentialType = credentialType;
        }

        public AuthenticationFailedException(string server, string credentialType, string message) 
            : base(message)
        {
            Server = server;
            CredentialType = credentialType;
        }

        public AuthenticationFailedException(string server, string credentialType, string message, Exception innerException) 
            : base(message, innerException)
        {
            Server = server;
            CredentialType = credentialType;
        }
    }
}
