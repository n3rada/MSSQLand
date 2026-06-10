// MSSQLand/Services/UserService.cs

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SqlClient;
using System.Linq;

using MSSQLand.Exceptions;
using MSSQLand.Utilities;

namespace MSSQLand.Services
{
    public class UserService
    {
        private readonly QueryService _queryService;

        /// <summary>
        /// Dictionary to cache admin status for each execution server.
        /// </summary>
        private readonly ConcurrentDictionary<string, bool> _adminStatusCache = new();

        /// <summary>
        /// Dictionary to cache domain user status for each execution server.
        /// </summary>
        private readonly ConcurrentDictionary<string, bool> _isDomainUserCache = new();

        /// <summary>
        /// Dictionary to cache server-level permission checks, keyed by "hostname:permission".
        /// </summary>
        private readonly ConcurrentDictionary<string, bool> _permissionCache = new(StringComparer.OrdinalIgnoreCase);

        public string MappedUser { get; private set; }
        public string SystemUser { get; private set; }
        public string EffectiveUser { get; private set; }
        public string SourcePrincipal { get; private set; }

        public bool IsDomainUser
        {
            get
            {
                // Check if the domain user status is already cached for the current ExecutionServer
                if (_isDomainUserCache.TryGetValue(_queryService.ExecutionServer.Hostname, out bool isDomainUser))
                {
                    return isDomainUser;
                }

                // If not cached, compute and store the result
                bool domainUserStatus = CheckIfDomainUser();

                // Cache the result for the current ExecutionServer
                _isDomainUserCache[_queryService.ExecutionServer.Hostname] = domainUserStatus;

                return domainUserStatus;
            }
        }


        public UserService(QueryService queryService)
        {
            _queryService = queryService;
        }

        /// <summary>
        /// Returns true if the login is a Windows system account (NT AUTHORITY\, NT SERVICE\, etc.).
        /// These accounts add no unique linked server mapping information and often cause database access errors.
        /// </summary>
        public static bool IsSystemAccount(string login)
        {
            return !string.IsNullOrEmpty(login) &&
                   (login.StartsWith("NT ", StringComparison.OrdinalIgnoreCase) ||
                    login.StartsWith("NT\\", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Clears cached admin and domain user status.
        /// Call this when the execution context changes (e.g., after modifying the linked server chain).
        /// </summary>
        public void ClearCaches()
        {
            _adminStatusCache.Clear();
            _isDomainUserCache.Clear();
            _permissionCache.Clear();
        }

        public bool IsAdmin()
        {
            // Check if the admin status is already cached for the current ExecutionServer
            if (_adminStatusCache.TryGetValue(_queryService.ExecutionServer.Hostname, out bool isAdmin))
            {
                return isAdmin;
            }

            // Quick check: sa login is always sysadmin
            if (SystemUser.Equals("sa", StringComparison.OrdinalIgnoreCase))
            {
                _adminStatusCache[_queryService.ExecutionServer.Hostname] = true;
                return true;
            }

            // Otherwise check sysadmin role membership
            bool adminStatus = IsMemberOfRole("sysadmin");

            // Cache the result for the current ExecutionServer
            _adminStatusCache[_queryService.ExecutionServer.Hostname] = adminStatus;

            return adminStatus;
        }

        /// <summary>
        /// Checks whether the current login holds a specific server-level permission.
        /// Results are cached per execution server to avoid redundant round-trips.
        /// </summary>
        /// <param name="permission">Server-level permission name (e.g. "CONTROL SERVER", "ALTER ANY LOGIN").</param>
        /// <returns>True if the current login has the permission; otherwise false.</returns>
        public bool HasPermission(string permission)
        {
            string cacheKey = $"{_queryService.ExecutionServer.Hostname}:{permission}";
            if (_permissionCache.TryGetValue(cacheKey, out bool cached))
                return cached;

            bool result = false;
            try
            {
                result = Convert.ToInt32(_queryService.ExecuteScalar(
                    $"SELECT HAS_PERMS_BY_NAME(NULL, NULL, '{permission.Replace("'", "''")}')")) == 1;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Error checking permission '{permission}': {ex.Message}");
            }

            _permissionCache[cacheKey] = result;
            return result;
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
        /// <returns>A tuple containing the mapped user and system user.</returns>
        public (string MappedUser, string SystemUser) GetInfo()
        {
            const string query = "SELECT USER_NAME() AS U, SYSTEM_USER AS S;";

            string mappedUser = "";
            string systemUser = "";

            // Close the reader before executing subsequent queries
            using (var reader = _queryService.Execute(query))
            {
                if (reader.Read())
                {
                    mappedUser = reader["U"]?.ToString() ?? "";
                    systemUser = reader["S"]?.ToString() ?? "";
                }
            } // DataReader is closed here

            // Update the properties
            this.MappedUser = mappedUser;
            this.SystemUser = systemUser;

            return (mappedUser, systemUser);
        }

        /// <summary>
        /// Gets the effective database user and the source principal (AD group or login) that granted access.
        /// This handles cases where access is granted through AD group membership
        /// rather than direct login mapping (e.g., DOMAIN\User -> AD Group -> Database User).
        /// Uses the token from integrated Windows authentication.
        ///
        /// IMPORTANT: Only works on direct connections. Does NOT work through linked servers
        /// as sys.login_token is not available in remote execution contexts.
        ///
        /// https://learn.microsoft.com/fr-fr/sql/relational-databases/system-catalog-views/sys-login-token-transact-sql
        /// </summary>
        /// <returns>Tuple of (EffectiveUser, SourcePrincipal)</returns>
        public void ComputeEffectiveUserAndSource()
        {
            try
            {
                this.EffectiveUser = MappedUser;

                // Check if SYSTEM_USER has a direct Windows login (type 'U') in sys.server_principals.
                // This is a single indexed lookup, cheap for the common case.
                object type = _queryService.ExecuteScalar(
                    "SELECT type FROM sys.server_principals WHERE name = SYSTEM_USER;");

                if (type?.ToString() == "U")
                {
                    this.SourcePrincipal = SystemUser;
                    return;
                }

                // No direct login; access granted via an AD group.
                // Find the group in sys.login_token joined to sys.server_principals.
                object group = _queryService.ExecuteScalar(@"
SELECT TOP 1 sp.name
FROM sys.login_token lt
INNER JOIN sys.server_principals sp ON sp.sid = lt.sid
WHERE lt.type = 'WINDOWS GROUP' AND sp.type = 'G'
ORDER BY sp.principal_id;");

                this.SourcePrincipal = group?.ToString() ?? SystemUser;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Error determining effective user and source: {ex.Message}");
                this.EffectiveUser = MappedUser;
                this.SourcePrincipal = SystemUser;
            }
        }



        /// <summary>
        /// Checks if the current system user is a Windows domain user.
        /// Uses username format (DOMAIN\username) as primary check.
        /// Linked server connections don't have sys.login_token, so format check is more reliable.
        /// </summary>
        /// <returns>True if the user is a Windows domain user; otherwise, false.</returns>
        private bool CheckIfDomainUser()
        {
            if (string.IsNullOrEmpty(SystemUser))
            {
                Logger.Trace($"CheckIfDomainUser: SystemUser is null or empty");
                return false;
            }


            // Check if username has the DOMAIN\username format
            int backslashIndex = SystemUser.IndexOf('\\');

            if (backslashIndex <= 0 || backslashIndex >= SystemUser.Length - 1)
            {
                // No backslash or invalid format - not a domain user
                return false;
            }

            // Username has domain format - it's a Windows user
            return true;
        }

        /// <summary>
        /// Returns the current user's server role memberships split into fixed and custom roles.
        /// Excludes the public role and internal placeholder roles (##...##).
        ///
        /// Side effect: populates the admin-status cache based on whether the result contains
        /// 'sysadmin'. This means a subsequent <see cref="IsAdmin"/> call against the same
        /// execution context becomes a cache hit, eliminating a redundant round-trip when
        /// callers need both pieces of information (a common pattern during link-map
        /// exploration with deep EXEC AT nesting).
        /// </summary>
        public (List<string> Fixed, List<string> Custom) GetServerRoles()
        {
            const string query = @"
SELECT name, is_fixed_role
FROM sys.server_principals
WHERE type = 'R'
  AND name != 'public'
  AND name NOT LIKE '##%##'
  AND ISNULL(IS_SRVROLEMEMBER(name), 0) = 1
ORDER BY is_fixed_role DESC, name;";

            var fixedRoles = new List<string>();
            var customRoles = new List<string>();
            try
            {
                var rolesTable = _queryService.ExecuteTable(query);
                foreach (System.Data.DataRow row in rolesTable.Rows)
                {
                    string name = row["name"].ToString();
                    if (Convert.ToBoolean(row["is_fixed_role"]))
                        fixedRoles.Add(name);
                    else
                        customRoles.Add(name);
                }

                bool isSysadmin = fixedRoles.Exists(r => r.Equals("sysadmin", StringComparison.OrdinalIgnoreCase));
                _adminStatusCache[_queryService.ExecutionServer.Hostname] = isSysadmin;
            }
            catch
            {
                // Role query failed; do NOT populate the admin cache.
            }
            return (fixedRoles, customRoles);
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
                Logger.Info($"You can impersonate anyone on {_queryService.ExecutionServer.Hostname} as a sysadmin");
                return true;
            }

            string safeUser = user.Replace("'", "''");
            string query = $"SELECT 1 FROM master.sys.server_permissions a INNER JOIN master.sys.server_principals b ON a.grantor_principal_id = b.principal_id WHERE a.permission_name = 'IMPERSONATE' AND b.name = '{safeUser}';";

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
        /// Impersonates a specified login.
        /// For direct connections: issues EXECUTE AS LOGIN that persists in the session.
        /// For linked servers: updates the impersonation arrays prepended to every query
        /// (EXECUTE AS doesn't persist across separate EXEC() AT calls).
        /// Throws ImpersonationFailedException if impersonation fails on direct connections.
        /// </summary>
        /// <param name="user">The login to impersonate.</param>
        public void ImpersonateUser(string user)
        {
            if (!_queryService.LinkedServers.IsEmpty)
            {
                PushLinkedImpersonation(user);
                _adminStatusCache.Clear();
                return;
            }

            string impersonateQuery = $"EXECUTE AS LOGIN = N'{user.Replace("'", "''")}';";
            try
            {
                using var command = _queryService.Connection.CreateCommand();
                command.CommandText = impersonateQuery;
                command.ExecuteNonQuery();
                _adminStatusCache.Clear();
                Logger.Debug($"Impersonated user {user} for current connection");
            }
            catch (Exception ex) when (ex.Message.Contains("916") || (ex is SqlException sqlex && sqlex.Number == 916))
            {
                Logger.Debug($"Switching to master before impersonating '{user}'");
                Logger.DebugNested(ex.Message);
                try
                {
                    using (var useMaster = _queryService.Connection.CreateCommand())
                    {
                        useMaster.CommandText = "USE master;";
                        useMaster.ExecuteNonQuery();
                    }

                    using var command = _queryService.Connection.CreateCommand();
                    command.CommandText = impersonateQuery;
                    command.ExecuteNonQuery();
                    _adminStatusCache.Clear();
                    Logger.Debug($"Impersonated user {user} for current connection (via master)");
                }
                catch (Exception retryEx)
                {
                    throw new ImpersonationFailedException(user, $"Failed to impersonate user '{user}': {retryEx.Message}", retryEx);
                }
            }
            catch (Exception ex)
            {
                throw new ImpersonationFailedException(user, $"Failed to impersonate user '{user}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Attempts to impersonate a specified login without throwing on failure.
        /// </summary>
        /// <param name="user">The login to impersonate.</param>
        /// <returns>True if impersonation succeeded; false otherwise.</returns>
        public bool TryImpersonateUser(string user)
        {
            try
            {
                ImpersonateUser(user);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Reverts the last impersonation hop.
        /// For direct connections: issues a REVERT command.
        /// For linked servers: pops the last login from the impersonation arrays.
        /// </summary>
        public void RevertImpersonation()
        {
            if (!_queryService.LinkedServers.IsEmpty)
            {
                PopLinkedImpersonation();
                _adminStatusCache.Clear();
                return;
            }

            using var command = _queryService.Connection.CreateCommand();
            command.CommandText = "REVERT;";
            command.ExecuteNonQuery();

            _adminStatusCache.Clear();
            Logger.Debug("Reverted impersonation");
        }

        /// <summary>
        /// Pushes a login onto the linked server impersonation chain (last server in the chain).
        /// </summary>
        private void PushLinkedImpersonation(string login)
        {
            int lastIdx = _queryService.LinkedServers.ServerChain.Length - 1;
            string[] current = _queryService.LinkedServers.ServerChain[lastIdx].ImpersonationUsers;

            var updated = new List<string>();
            if (current != null) updated.AddRange(current);
            updated.Add(login);

            string[] updatedArray = updated.ToArray();
            _queryService.LinkedServers.ServerChain[lastIdx].ImpersonationUsers = updatedArray;
            _queryService.LinkedServers.ComputableImpersonationUsers[lastIdx] = updatedArray;

            Logger.Trace($"Linked impersonation chain set to: [{string.Join(" -> ", updated)}]");
        }

        /// <summary>
        /// Pops the last login from the linked server impersonation chain (last server in the chain).
        /// </summary>
        private void PopLinkedImpersonation()
        {
            int lastIdx = _queryService.LinkedServers.ServerChain.Length - 1;
            string[] current = _queryService.LinkedServers.ServerChain[lastIdx].ImpersonationUsers;
            string[] restored = null;

            if (current != null && current.Length > 1)
            {
                restored = current.Take(current.Length - 1).ToArray();
            }

            _queryService.LinkedServers.ServerChain[lastIdx].ImpersonationUsers = restored;
            _queryService.LinkedServers.ComputableImpersonationUsers[lastIdx] = restored ?? Array.Empty<string>();

            Logger.Trace($"Linked impersonation reverted to: [{(restored != null ? string.Join(" -> ", restored) : "none")}]");
        }
    }
}
