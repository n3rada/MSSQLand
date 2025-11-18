using MSSQLand.Utilities;
using System;
using System.Collections.Concurrent;
using System.Data.SqlClient;

namespace MSSQLand.Services
{
    public class UserService
    {
        private readonly QueryService _queryService;

        /// <summary>
        /// Dictionary to cache admin status for each execution server.
        /// </summary>
        private readonly ConcurrentDictionary<string, bool> _adminStatusCache = new();

        public string MappedUser { get; private set; }
        public string SystemUser { get; private set; }


        public UserService(QueryService queryService)
        {
            _queryService = queryService;
        }

        public bool IsAdmin()
        {
            // Check if the admin status is already cached for the current ExecutionServer
            if (_adminStatusCache.TryGetValue(_queryService.ExecutionServer, out bool isAdmin))
            {
                return isAdmin;
            }

            // If not cached, compute and store the result
            bool adminStatus = IsMemberOfRole("sysadmin");

            // Cache the result for the current ExecutionServer
            _adminStatusCache[_queryService.ExecutionServer] = adminStatus;

            return adminStatus;
        }

        /// <summary>
        /// Checks if the current user is a member of a specified server role.
        /// </summary>
        /// <param name="connection">An open SQL connection to use for the query.</param>
        /// <param name="role">The role to check (e.g., 'sysadmin').</param>
        /// <returns>True if the user is a member of the role; otherwise, false.</returns>
        public bool IsMemberOfRole(string role)
        {
            try
            {
                return Convert.ToInt32(_queryService.ExecuteScalar($"SELECT IS_SRVROLEMEMBER('{role}');")) == 1;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Error checking role membership for role {role}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Retrieves information about the current user, including the username and system user.
        /// </summary>
        /// <param name="connection">An open SQL connection to use for the query.</param>
        /// <returns>A tuple containing the username and system user.</returns>
        public (string MappedUser, string SystemUser) GetInfo()
        {
            const string query = "SELECT USER_NAME() AS U, SYSTEM_USER AS S;";

            string mappedUser = "";
            string systemUser = "";

            using var reader = _queryService.Execute(query);

            if (reader.Read())
            {
                mappedUser = reader["U"]?.ToString() ?? "Unknown";
                systemUser = reader["S"]?.ToString() ?? "Unknown";
            }

            // Update the properties
            this.MappedUser = mappedUser;
            this.SystemUser = systemUser;

            return (mappedUser, systemUser);
        }

        /// <summary>
        /// Checks if the current user can impersonate a specified login.
        /// </summary>
        /// <param name="user">The login to check for impersonation.</param>
        /// <returns>True if the user can impersonate the specified login; otherwise, false.</returns>
        public bool CanImpersonate(string user)
        {
            // A sysadmin user can impersonate anyone
            if (IsAdmin())
            {
                Logger.Info($"You can impersonate anyone on {_queryService.ExecutionServer} as a sysadmin");
                return true;
            }

            string query = $"SELECT 1 FROM master.sys.server_permissions a INNER JOIN master.sys.server_principals b ON a.grantor_principal_id = b.principal_id WHERE a.permission_name = 'IMPERSONATE' AND b.name = '{user}';";

            try
            {
                return Convert.ToInt32(_queryService.ExecuteScalar(query)) == 1;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Error checking impersonation for user {user}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Impersonates a specified user on the initial connection.
        /// </summary>
        /// <param name="user">The login to impersonate.</param>
        public void ImpersonateUser(string user)
        {
            const string query = "EXECUTE AS LOGIN = @User;";
            using var command = new SqlCommand(query, _queryService.Connection);
            command.Parameters.AddWithValue("@User", user);

            
            command.ExecuteNonQuery();
            Logger.Info($"Impersonated user {user} for current connection");

        }

        /// <summary>
        /// Reverts any active impersonation and restores the original login.
        /// </summary>
        public void RevertImpersonation()
        {
            const string query = "REVERT;";
            using var command = new SqlCommand(query, _queryService.Connection);
            command.ExecuteNonQuery();

            Logger.Info("Reverted impersonation, restored original login.");
        }
    }
}
