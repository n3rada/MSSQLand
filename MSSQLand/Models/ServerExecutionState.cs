// MSSQLand/Models/ServerExecutionState.cs

using MSSQLand.Services;
using MSSQLand.Utilities;

namespace MSSQLand.Models
{
    /// <summary>
    /// Represents the runtime execution state of a SQL Server connection.
    /// Used for loop detection in linked server chains by tracking the exact execution context.
    ///
    /// This is separate from <see cref="Server"/> (which represents connection configuration)
    /// to maintain clean separation of concerns:
    ///   Server = static config, ServerExecutionState = runtime state.
    /// </summary>
    public class ServerExecutionState : System.IEquatable<ServerExecutionState>
    {
        /// <summary>The hostname or IP address of the server.</summary>
        public string Hostname { get; }

        /// <summary>The mapped database user (from USER_NAME()).</summary>
        public string MappedUser { get; }

        /// <summary>The system login user (from SYSTEM_USER).</summary>
        public string SystemUser { get; }

        /// <summary>Whether the current user has sysadmin privileges.</summary>
        public bool IsSysadmin { get; }

        public ServerExecutionState(string hostname, string mappedUser, string systemUser, bool isSysadmin)
        {
            Hostname = hostname ?? "";
            MappedUser = mappedUser ?? "";
            SystemUser = systemUser ?? "";
            IsSysadmin = isSysadmin;
        }

        /// <summary>
        /// Factory method: creates a <see cref="ServerExecutionState"/> by querying the
        /// current identity from <paramref name="userService"/>.
        /// </summary>
        public static ServerExecutionState FromContext(string hostname, UserService userService)
        {
            var (mappedUser, systemUser) = userService.GetInfo();
            return new ServerExecutionState(hostname, mappedUser, systemUser, userService.IsAdmin());
        }

        /// <summary>
        /// Computes a SHA-256 hash encoding the execution context (server + identity + privilege).
        /// Used for loop detection during linked server exploration.
        /// </summary>
        public string GetStateHash()
        {
            string stateString =
                $"{Hostname.ToUpperInvariant()}|" +
                $"{MappedUser.ToUpperInvariant()}|" +
                $"{SystemUser.ToUpperInvariant()}|" +
                $"{IsSysadmin}";
            return ByteHelper.ComputeSHA256(stateString);
        }

        /// <summary>8-character hex prefix of <see cref="GetStateHash"/>, suitable for filenames.</summary>
        public string ShortHash => GetStateHash().Substring(0, 8);

        /// <inheritdoc/>
        public bool Equals(ServerExecutionState other)
        {
            if (other is null) return false;
            return string.Equals(Hostname, other.Hostname, System.StringComparison.OrdinalIgnoreCase)
                && string.Equals(MappedUser, other.MappedUser, System.StringComparison.OrdinalIgnoreCase)
                && string.Equals(SystemUser, other.SystemUser, System.StringComparison.OrdinalIgnoreCase)
                && IsSysadmin == other.IsSysadmin;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as ServerExecutionState);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (Hostname?.ToUpperInvariant()?.GetHashCode() ?? 0);
                hash = hash * 31 + (MappedUser?.ToUpperInvariant()?.GetHashCode() ?? 0);
                hash = hash * 31 + (SystemUser?.ToUpperInvariant()?.GetHashCode() ?? 0);
                hash = hash * 31 + IsSysadmin.GetHashCode();
                return hash;
            }
        }

        /// <inheritdoc/>
        public override string ToString()
            => $"{Hostname} (System: {SystemUser}, Mapped: {MappedUser}, Sysadmin: {IsSysadmin})";
    }
}
