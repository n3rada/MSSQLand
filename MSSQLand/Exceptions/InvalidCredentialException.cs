using System;

namespace MSSQLand.Exceptions
{
    /// <summary>
    /// Exception thrown when credential validation fails.
    /// </summary>
    public class InvalidCredentialException : Exception
    {
        public string CredentialType { get; }

        public InvalidCredentialException(string credentialType, string message) 
            : base(message)
        {
            CredentialType = credentialType;
        }
    }
}
