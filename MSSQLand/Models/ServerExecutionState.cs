using MSSQLand.Services;
using MSSQLand.Utilities;
using System;

namespace MSSQLand.Models
{
    /// <summary>
    /// Represents the runtime execution state of a SQL Server connection.
    /// Used for loop detection in linked server chains by tracking the exact execution context.
    /// 
    /// This is separate from Server (which represents connection configuration) to maintain
    /// clean separation of concerns: Server = static config, ServerExecutionState = runtime state.
    /// </summary>
    public class ServerExecutionState : IEquatable<ServerExecutionState>
    {
        /// <summary>
        /// The hostname or IP address of the server.
        /// </summary>
        public string Hostname { get; set; }

        /// <summary>
        /// The mapped database user (from USER_NAME()).
        /// </summary>
        public string MappedUser { get; set; }

        /// <summary>
        /// The system user (from SYSTEM_USER).
        /// </summary>
        public string SystemUser { get; set; }

        /// <summary>
        /// Whether the current user has sysadmin privileges on this server.
        /// This is crucial for loop detection because same user with different privileges
        /// represents a different execution capability.
        /// </summary>
        public bool IsSysadmin { get; set; }

        /// <summary>
        /// Factory method to create a ServerExecutionState from a DatabaseContext.
        /// Automatically queries the current user info and admin status.
        /// </summary>
        /// <param name="hostname">The server hostname</param>
        /// <param name="userService">The UserService to query current execution state</param>
        /// <returns>A new ServerExecutionState representing the current execution context</returns>
        public static ServerExecutionState FromContext(string hostname, UserService userService)
        {
            var (mappedUser, systemUser) = userService.GetInfo();
            
            return new ServerExecutionState
            {
                Hostname = hostname,
                MappedUser = mappedUser,
                SystemUser = systemUser,
                IsSysadmin = userService.IsAdmin()
            };
        }

        /// <summary>
        /// Computes a unique state hash for loop detection.
        /// Hash is based on: Hostname, MappedUser, SystemUser, and IsSysadmin.
        /// </summary>
        /// <returns>SHA-256 hash representing the execution state.</returns>
        public string GetStateHash()
        {
            string stateString = $"{Hostname?.ToUpperInvariant() ?? ""}" +
                                $"{MappedUser?.ToUpperInvariant() ?? ""}" +
                                $"{SystemUser?.ToUpperInvariant() ?? ""}" +
                                $"{IsSysadmin}";
            
            return Misc.ComputeSHA256(stateString);
        }

        /// <summary>
        /// Checks if two ServerExecutionState instances represent the same execution state.
        /// Used for loop detection in linked server chains.
        /// </summary>
        public bool Equals(ServerExecutionState other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;

            return string.Equals(Hostname, other.Hostname, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(MappedUser, other.MappedUser, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(SystemUser, other.SystemUser, StringComparison.OrdinalIgnoreCase) &&
                   IsSysadmin == other.IsSysadmin;
        }

        /// <summary>
        /// Override for object.Equals to support general equality checks.
        /// </summary>
        public override bool Equals(object obj)
        {
            return Equals(obj as ServerExecutionState);
        }

        /// <summary>
        /// Override GetHashCode to support HashSet and Dictionary operations.
        /// Two states with the same execution context will have the same hash code.
        /// </summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (Hostname?.ToUpperInvariant().GetHashCode() ?? 0);
                hash = hash * 31 + (MappedUser?.ToUpperInvariant().GetHashCode() ?? 0);
                hash = hash * 31 + (SystemUser?.ToUpperInvariant().GetHashCode() ?? 0);
                hash = hash * 31 + IsSysadmin.GetHashCode();
                return hash;
            }
        }

        /// <summary>
        /// Overload == operator for convenient equality checks.
        /// </summary>
        public static bool operator ==(ServerExecutionState left, ServerExecutionState right)
        {
            if (left is null) return right is null;
            return left.Equals(right);
        }

        /// <summary>
        /// Overload != operator for convenient inequality checks.
        /// </summary>
        public static bool operator !=(ServerExecutionState left, ServerExecutionState right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Returns a string representation of the execution state for debugging.
        /// </summary>
        public override string ToString()
        {
            return $"{Hostname} (System: {SystemUser}, Mapped: {MappedUser}, Sysadmin: {IsSysadmin})";
        }
    }
}
