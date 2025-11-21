using MSSQLand.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        
        /// <summary>
        /// Dictionary to cache domain user status for each execution server.
        /// </summary>
        private readonly ConcurrentDictionary<string, bool> _isDomainUserCache = new();
        
        /// <summary>
        /// Dictionary to cache AD group memberships for each execution server.
        /// </summary>
        private readonly ConcurrentDictionary<string, List<string>> _adGroupsCache = new();

        public string MappedUser { get; private set; }
        public string SystemUser { get; private set; }
        public string EffectiveUser { get; private set; }
        public string SourcePrincipal { get; private set; }
        
        public bool IsDomainUser 
        { 
            get
            {
                // Check if the domain user status is already cached for the current ExecutionServer
                if (_isDomainUserCache.TryGetValue(_queryService.ExecutionServer, out bool isDomainUser))
                {
                    return isDomainUser;
                }

                // If not cached, compute and store the result
                bool domainUserStatus = CheckIfDomainUser();

                // Cache the result for the current ExecutionServer
                _isDomainUserCache[_queryService.ExecutionServer] = domainUserStatus;

                return domainUserStatus;
            }
        }


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

            // Close the reader before executing subsequent queries
            using (var reader = _queryService.Execute(query))
            {
                if (reader.Read())
                {
                    mappedUser = reader["U"]?.ToString() ?? "Unknown";
                    systemUser = reader["S"]?.ToString() ?? "Unknown";
                }
            } // DataReader is closed here

            // Update the properties
            this.MappedUser = mappedUser;
            this.SystemUser = systemUser;
            
            // Compute effective user and source principal (handles group-based access)
            (this.EffectiveUser, this.SourcePrincipal) = GetEffectiveUserAndSource();

            return (mappedUser, systemUser);
        }

        /// <summary>
        /// Gets the effective database user and the source principal (AD group or login) that granted access.
        /// This handles cases where access is granted through AD group membership
        /// rather than direct login mapping (e.g., DOMAIN\User -> AD Group -> Database User).
        /// This is the authorization identity used by SQL Server.
        /// </summary>
        /// <returns>Tuple of (EffectiveUser, SourcePrincipal)</returns>
        private (string EffectiveUser, string SourcePrincipal) GetEffectiveUserAndSource()
        {
            try
            {
                // If there's a direct mapping (MappedUser != SystemUser), use it
                if (!MappedUser.Equals(SystemUser, StringComparison.OrdinalIgnoreCase))
                {
                    return (MappedUser, SystemUser);
                }

                // Query user_token to find effective database user and login_token for source
                string sql = @"
                    SELECT TOP 1
                        dp.name AS effective_user,
                        lt.name AS source_principal
                    FROM sys.user_token ut
                    JOIN sys.database_principals dp ON dp.sid = ut.sid
                    LEFT JOIN sys.login_token lt ON lt.sid = ut.sid
                    WHERE ut.name <> 'public'
                    AND ut.type NOT IN ('ROLE', 'SERVER ROLE')
                    AND dp.principal_id > 0
                    ORDER BY dp.principal_id;";

                var dt = _queryService.ExecuteTable(sql);

                if (dt.Rows.Count == 0)
                    return (MappedUser, SystemUser);

                var row = dt.Rows[0];
                string effective = row["effective_user"]?.ToString() ?? MappedUser;
                string source = row["source_principal"]?.ToString() ?? effective;

                return (effective, source);
            }
            catch (Exception ex)
            {
                Logger.Warning($"Error determining effective user and source: {ex.Message}");
                return (MappedUser, SystemUser);
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
        /// Retrieves the list of AD groups the current user is a member of.
        /// Uses sys.login_token which contains the Windows authentication token groups.
        /// These are the groups authenticated by the domain controller at login time.
        /// For linked server connections where sys.login_token is unavailable, falls back to
        /// checking IS_MEMBER against server principals.
        /// Results are cached per execution server.
        /// </summary>
        /// <returns>List of AD group names the user belongs to, or empty list if none found.</returns>
        public List<string> GetUserAdGroups()
        {
            // Check if groups are already cached for the current ExecutionServer
            if (_adGroupsCache.TryGetValue(_queryService.ExecutionServer, out List<string> cachedGroups))
            {
                return cachedGroups;
            }

            var groups = new List<string>();

            if (string.IsNullOrEmpty(SystemUser) || !IsDomainUser)
            {
                _adGroupsCache[_queryService.ExecutionServer] = groups;
                return groups;
            }

            try
            {
                // Try sys.login_token first (works for direct connections)
                string tokenQuery = @"
                    SELECT name
                    FROM sys.login_token
                    WHERE type = 'WINDOWS GROUP'
                    AND principal_id > 0
                    ORDER BY name;";

                var tokenTable = _queryService.ExecuteTable(tokenQuery);

                if (tokenTable.Rows.Count > 0)
                {
                    // Direct connection - we have the login token
                    foreach (System.Data.DataRow row in tokenTable.Rows)
                    {
                        string groupName = row["name"].ToString();
                        groups.Add(groupName);
                    }
                }
                else
                {
                    // Linked server or token unavailable - fall back to IS_MEMBER
                    string groupsQuery = @"
                        SELECT name
                        FROM master.sys.server_principals
                        WHERE type = 'G'
                        AND name LIKE '%\%'
                        AND name NOT LIKE '##%'
                        ORDER BY name;";

                    var serverGroups = _queryService.ExecuteTable(groupsQuery);

                    foreach (System.Data.DataRow row in serverGroups.Rows)
                    {
                        string groupName = row["name"].ToString();

                        try
                        {
                            string memberCheckQuery = $"SELECT IS_MEMBER('{groupName.Replace("'", "''")}');";
                            object result = _queryService.ExecuteScalar(memberCheckQuery);

                            if (result != null && result != DBNull.Value && Convert.ToInt32(result) == 1)
                            {
                                groups.Add(groupName);
                            }
                        }
                        catch
                        {
                            // IS_MEMBER might fail for some groups
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Error retrieving AD groups: {ex.Message}");
            }

            // Cache the result
            _adGroupsCache[_queryService.ExecutionServer] = groups;
            return groups;
        }


        /// <summary>
        /// Retrieves the list of database roles the current user is a member of.
        /// Checks roles in the current database context.
        /// </summary>
        /// <returns>List of database role names the user belongs to, or empty list if none found.</returns>
        public List<string> GetUserDatabaseRoles()
        {
            var roles = new List<string>();

            try
            {
                // Get all database roles that the current user is a member of
                string rolesQuery = @"
                    SELECT r.name
                    FROM sys.database_principals r
                    INNER JOIN sys.database_role_members rm ON r.principal_id = rm.role_principal_id
                    INNER JOIN sys.database_principals m ON rm.member_principal_id = m.principal_id
                    WHERE m.name = USER_NAME()
                    AND r.type = 'R'
                    ORDER BY r.name;";

                var rolesTable = _queryService.ExecuteTable(rolesQuery);

                foreach (System.Data.DataRow row in rolesTable.Rows)
                {
                    string roleName = row["name"].ToString();
                    roles.Add(roleName);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Error retrieving database roles: {ex.Message}");
            }

            return roles;
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
