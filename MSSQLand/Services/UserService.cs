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
        /// Checks if the current system user is a Windows domain user.
        /// Logic: If the username contains a backslash AND is not a SQL_LOGIN, it's a domain user.
        /// This handles both direct logins and group-based access.
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

            try
            {
                // Check if this is a SQL login (not Windows authentication)
                string checkQuery = $"SELECT type_desc FROM master.sys.server_principals WHERE name = '{SystemUser.Replace("'", "''")}';";
                object result = _queryService.ExecuteScalar(checkQuery);
                
                if (result != null && result != DBNull.Value)
                {
                    string typeDesc = result.ToString();
                    // If it's a SQL_LOGIN, then it's NOT a domain user (even if it has a backslash)
                    return !typeDesc.Equals("SQL_LOGIN", StringComparison.OrdinalIgnoreCase);
                }
                
                // User not found in sys.server_principals (group-based access) - it's a domain user
                return true;
            }
            catch
            {
                // If query fails, assume backslash means domain user
                return true;
            }
        }

        /// <summary>
        /// Retrieves the list of AD groups the current user is a member of.
        /// Uses IS_MEMBER() to check all Windows groups in SQL Server.
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
                // Try xp_logininfo first (most comprehensive but requires privileges)
                try
                {
                    string query = $"EXEC master.dbo.xp_logininfo @acctname = '{SystemUser.Replace("'", "''")}', @option = 'all';";
                    var groupsTable = _queryService.ExecuteTable(query);

                    if (groupsTable != null && groupsTable.Rows.Count > 0)
                    {
                        foreach (System.Data.DataRow row in groupsTable.Rows)
                        {
                            string type = row["type"]?.ToString();
                            if (!string.IsNullOrEmpty(type) && 
                                type.IndexOf("group", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                string groupName = row["permission path"]?.ToString() ?? row["account name"]?.ToString();
                                if (!string.IsNullOrEmpty(groupName))
                                {
                                    groups.Add(groupName);
                                }
                            }
                        }

                        // Cache and return
                        _adGroupsCache[_queryService.ExecutionServer] = groups;
                        return groups;
                    }
                }
                catch
                {
                    // xp_logininfo not available, fall through to IS_MEMBER approach
                }

                // Fallback: Use IS_MEMBER() to check all Windows groups
                string groupsQuery = @"
                    SELECT name
                    FROM master.sys.server_principals
                    WHERE type = 'G'
                    AND name LIKE '%\%'
                    AND name NOT LIKE '##%'
                    ORDER BY name;";

                var windowsGroups = _queryService.ExecuteTable(groupsQuery);

                foreach (System.Data.DataRow row in windowsGroups.Rows)
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
            catch (Exception ex)
            {
                Logger.Warning($"Error retrieving AD groups: {ex.Message}");
            }

            // Cache the result
            _adGroupsCache[_queryService.ExecutionServer] = groups;
            return groups;
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
