using System;

namespace MSSQLand.Exceptions
{
    /// <summary>
    /// Exception thrown when SQL Server user impersonation fails.
    /// </summary>
    public class ImpersonationFailedException : Exception
    {
        public string TargetUser { get; }

        public ImpersonationFailedException(string targetUser) 
            : base($"Failed to impersonate user '{targetUser}'.")
        {
            TargetUser = targetUser;
        }

        public ImpersonationFailedException(string targetUser, string message) 
            : base(message)
        {
            TargetUser = targetUser;
        }

        public ImpersonationFailedException(string targetUser, string message, Exception innerException) 
            : base(message, innerException)
        {
            TargetUser = targetUser;
        }
    }
}
