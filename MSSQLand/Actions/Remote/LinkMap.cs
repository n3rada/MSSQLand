// MSSQLand/Actions/Remote/LinkMap.cs

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using MSSQLand.Actions.Database;
using MSSQLand.Models;
using MSSQLand.Services;
using MSSQLand.Utilities;

namespace MSSQLand.Actions.Remote
{
    /// <summary>
    /// Recursively explores all accessible linked server chains, mapping execution paths.
    /// Uses a tree structure for efficient storage and cleaner output.
    /// </summary>
    internal class LinkMap : BaseAction
    {
        /// <summary>
        /// Represents a node in the linked server tree.
        /// </summary>
        private class ServerNode
        {
            public string Alias { get; set; }
            public string ActualName { get; set; }
            public string LoggedInUser { get; set; }
            public string MappedUser { get; set; }
            public string ImpersonatedUser { get; set; }
            public bool IsSysadmin { get; set; }
            public List<string> ServerRoles { get; set; } = new();
            public List<string> NonSqlLinks { get; set; } = new();
            public List<ServerNode> Children { get; set; } = new();
        }

        [ExcludeFromArguments]
        private ServerNode _rootNode;

        /// <summary>
        /// Global set of server aliases that have been fully explored.
        /// Once a server is explored via any path, we don't need to explore it again
        /// since it will have the same linked servers regardless of how we reached it.
        /// This prevents combinatorial explosion in mesh topologies.
        /// </summary>
        [ExcludeFromArguments]
        private readonly HashSet<string> _globallyExploredServers = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Tracks all discovered chains for programmatic access.
        /// </summary>
        [ExcludeFromArguments]
        private readonly List<List<ServerNode>> _allChains = new();

        [ArgumentMetadata(Position = 0, Description = "Maximum recursion depth (default: 5, max: 15)")]
        private int _limit = 5;

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);

            if (_limit < 1 || _limit > 15)
            {
                throw new ArgumentException("Limit must be between 1 and 15.");
            }
        }

        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Maximum recursion depth: {_limit}");

            // Get initial linked servers from the starting context
            DataTable allLinkedServers;
            try
            {
                allLinkedServers = Links.GetLinkedServers(databaseContext);
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to query initial linked servers: {ex.Message}");
                return null;
            }

            if (allLinkedServers.Rows.Count == 0)
            {
                Logger.Warning("No linked servers found.");
                return null;
            }

            // Separate SQL Server links (chainable) from others (queryable only)
            var sqlServerLinks = new List<DataRow>();
            var otherLinks = new List<DataRow>();

            foreach (DataRow row in allLinkedServers.Rows)
            {
                string provider = row["Provider"].ToString();
                if (provider.StartsWith("SQLNCLI") || provider.StartsWith("MSOLEDBSQL"))
                {
                    sqlServerLinks.Add(row);
                }
                else
                {
                    otherLinks.Add(row);
                }
            }

            Logger.TaskNested($"SQL Server linked servers (chainable): {sqlServerLinks.Count}");

            // Create root node representing the starting server
            _rootNode = new ServerNode
            {
                Alias = databaseContext.Server.Hostname,
                ActualName = databaseContext.Server.Hostname,
                LoggedInUser = databaseContext.Server.SystemUser,
                MappedUser = databaseContext.Server.MappedUser,
                ImpersonatedUser = null,
                IsSysadmin = databaseContext.UserService.IsAdmin(),
                ServerRoles = GetUserServerRoles(databaseContext)
            };

            // Add non-SQL linked servers at initial server
            foreach (DataRow row in otherLinks)
            {
                string name = row["Link"].ToString();
                string provider = row["Provider"].ToString();
                _rootNode.NonSqlLinks.Add($"{name} ({provider})");
            }

            if (otherLinks.Count > 0)
            {
                Logger.Info($"Other linked servers (queryable via OPENQUERY):");
                foreach (DataRow row in otherLinks)
                {
                    string name = row["Link"].ToString();
                    string provider = row["Provider"].ToString();
                    string product = row["Product"].ToString();
                    Logger.InfoNested($"{name} ({provider}) - {product}");
                }
            }

            if (sqlServerLinks.Count == 0)
            {
                Logger.Warning("No SQL Server linked servers to explore.");
                return null;
            }

            // Mark starting server as explored
            _globallyExploredServers.Add(databaseContext.Server.Hostname);

            // Compute starting server's state hash for loop detection
            string startingHash = BuildStateHash(
                databaseContext.Server.Hostname,
                _rootNode.MappedUser,
                _rootNode.LoggedInUser,
                _rootNode.IsSysadmin
            );

            // Build complete map of reachable SQL links across all transitive impersonation chains.
            // Key: server name, Value: (row with link info, chain needed to reach that login context)
            var reachableChains = GetReachableLoginChains(databaseContext);
            var allSqlLinks = new Dictionary<string, (DataRow row, List<string> chain)>(StringComparer.OrdinalIgnoreCase);

            // Current user's links (already filtered to SQL above)
            foreach (DataRow row in sqlServerLinks)
            {
                string link = row["Link"].ToString();
                if (!allSqlLinks.ContainsKey(link) || string.IsNullOrEmpty(allSqlLinks[link].row["Local Login"]?.ToString()))
                    allSqlLinks[link] = (row, null);
            }

            // Additional links visible from each transitive impersonation chain
            foreach (var chain in reachableChains)
            {
                if (!TryApplyImpersonationChain(databaseContext, chain))
                    continue;
                try
                {
                    DataTable chainLinks = Links.GetLinkedServers(databaseContext);
                    foreach (DataRow row in chainLinks.Rows)
                    {
                        string provider = row["Provider"].ToString();
                        if (!provider.StartsWith("SQLNCLI") && !provider.StartsWith("MSOLEDBSQL"))
                            continue;
                        string link = row["Link"].ToString();
                        // Prefer rows that have a login mapping (more specific)
                        if (!allSqlLinks.ContainsKey(link) || string.IsNullOrEmpty(allSqlLinks[link].row["Local Login"]?.ToString()))
                            allSqlLinks[link] = (row, chain);
                    }
                }
                finally
                {
                    RevertChain(databaseContext, chain.Count);
                }
            }

            Logger.TaskNested($"Total reachable SQL Server linked servers: {allSqlLinks.Count}");

            // Explore each discovered SQL link
            foreach (var kvp in allSqlLinks)
            {
                string remoteServer = kvp.Key;
                var (linkRow, chainToReach) = kvp.Value;
                string requiredLogin = linkRow["Local Login"] == DBNull.Value ? "" : linkRow["Local Login"].ToString();

                if (_globallyExploredServers.Contains(remoteServer))
                {
                    Logger.TraceNested($"Server '{remoteServer}' already explored. Skipping.");
                    continue;
                }

                HashSet<string> visitedInChain = new() { startingHash };
                DatabaseContext tempContext = databaseContext.Duplicate();
                List<ServerNode> currentPath = new();

                // Apply the full impersonation chain needed to reach the right login context
                if (chainToReach != null && chainToReach.Count > 0)
                {
                    if (!TryApplyImpersonationChain(tempContext, chainToReach))
                    {
                        Logger.TraceNested($"Cannot apply impersonation chain for link to '{remoteServer}'. Skipping.");
                        continue;
                    }
                }
                else if (!string.IsNullOrEmpty(requiredLogin) && !requiredLogin.Equals(tempContext.Server.SystemUser, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        tempContext.QueryService.ExecuteNonProcessing($"EXECUTE AS LOGIN = '{requiredLogin.Replace("'", "''")}';" );
                    }
                    catch
                    {
                        Logger.TraceNested($"Cannot impersonate '{requiredLogin}' on starting server. Skipping link to '{remoteServer}'.");
                        continue;
                    }
                }

                ExploreLinkedServer(tempContext, remoteServer, _rootNode, currentPath, visitedInChain, currentDepth: 0);
            }

            // Count total chains
            int totalChains = CountLeafNodes(_rootNode);

            if (totalChains == 0)
            {
                Logger.Warning("No accessible linked server chains found.");
                return null;
            }

            Logger.Success($"Found {totalChains} accessible chain(s)");

            // Display tree view
            Logger.NewLine();
            DisplayTree();

            // Display chain commands summary
            Logger.NewLine();
            DisplayChainCommands();

            return _allChains;
        }

        /// <summary>
        /// Recursively explores linked servers, building a tree structure.
        /// At each server, dynamically discovers what users can be impersonated and what linked servers they have access to.
        /// </summary>
        private void ExploreLinkedServer(DatabaseContext databaseContext, string targetServer,
            ServerNode parentNode, List<ServerNode> currentPath, HashSet<string> visitedInChain, int currentDepth)
        {
            if (currentDepth >= _limit)
            {
                Logger.TraceNested($"Limit {_limit} reached at server '{targetServer}'. Backtracking.");
                return;
            }

            try
            {
                // Add server to chain
                databaseContext.QueryService.LinkedServers.AddToChain(targetServer);

                // Query actual server name
                string actualServerName = targetServer;
                try
                {
                    string serverNameResult = databaseContext.QueryService.ExecuteScalar("SELECT @@SERVERNAME")?.ToString();
                    if (!string.IsNullOrEmpty(serverNameResult))
                    {
                        actualServerName = serverNameResult;
                    }
                }
                catch
                {
                    // Keep alias as fallback
                }

                // Query user info through the chain
                var (mappedUser, remoteLoggedInUser) = databaseContext.UserService.GetInfo();
                Logger.TraceNested($"Logged in to server '{targetServer}' (actual: {actualServerName}) as: '{remoteLoggedInUser}' [{mappedUser}]");

                // Create state hash for loop detection
                bool isSysadmin = databaseContext.UserService.IsAdmin();
                string stateHash = BuildStateHash(targetServer, mappedUser, remoteLoggedInUser, isSysadmin);

                // Check for loop in THIS chain path only
                if (visitedInChain.Contains(stateHash))
                {
                    Logger.TraceNested($"Loop detected at server '{targetServer}' with user '{remoteLoggedInUser}'. Skipping to prevent infinite recursion.");
                    return;
                }

                visitedInChain.Add(stateHash);

                // Create node for this server
                var currentNode = new ServerNode
                {
                    Alias = targetServer,
                    ActualName = actualServerName,
                    LoggedInUser = remoteLoggedInUser,
                    MappedUser = mappedUser,
                    ImpersonatedUser = null,
                    IsSysadmin = isSysadmin,
                    ServerRoles = GetUserServerRoles(databaseContext)
                };

                // Add to parent's children
                parentNode.Children.Add(currentNode);

                // Track the path for chain commands
                var newPath = new List<ServerNode>(currentPath) { currentNode };
                _allChains.Add(newPath);

                // Mark this server as globally explored
                _globallyExploredServers.Add(targetServer);

                // Build complete map of reachable links via all transitive impersonation chains.
                // Key: server name, Value: (row with link info, chain needed to reach that context)
                var remoteReachableChains = GetReachableLoginChains(databaseContext);
                Logger.TraceNested($"Reachable login chains on '{targetServer}': {remoteReachableChains.Count}");

                var allLinkedServersOnThisServer = new Dictionary<string, (DataRow row, List<string> chain)>(StringComparer.OrdinalIgnoreCase);

                // Current user's links
                try
                {
                    DataTable currentUserLinks = Links.GetLinkedServers(databaseContext);
                    foreach (DataRow row in currentUserLinks.Rows)
                    {
                        string serverLink = row["Link"].ToString();
                        if (!allLinkedServersOnThisServer.ContainsKey(serverLink) || string.IsNullOrEmpty(allLinkedServersOnThisServer[serverLink].row["Local Login"]?.ToString()))
                            allLinkedServersOnThisServer[serverLink] = (row, null);
                    }
                }
                catch (Exception ex)
                {
                    Logger.TraceNested($"Failed to query linked servers on '{targetServer}' as current user: {ex.Message}");
                }

                // Additional links visible from each transitive impersonation chain
                foreach (var chain in remoteReachableChains)
                {
                    if (!TryApplyImpersonationChain(databaseContext, chain))
                        continue;
                    try
                    {
                        DataTable chainLinks = Links.GetLinkedServers(databaseContext);
                        foreach (DataRow row in chainLinks.Rows)
                        {
                            string serverLink = row["Link"].ToString();
                            // Prefer rows that have a login mapping (more specific)
                            if (!allLinkedServersOnThisServer.ContainsKey(serverLink) || string.IsNullOrEmpty(allLinkedServersOnThisServer[serverLink].row["Local Login"]?.ToString()))
                                allLinkedServersOnThisServer[serverLink] = (row, chain);
                        }
                    }
                    finally
                    {
                        RevertChain(databaseContext, chain.Count);
                    }
                }

                // Classify discovered links
                var remoteSqlLinks = new List<(string server, string login, DataRow row, List<string> chain)>();
                var remoteOtherLinks = new List<string>();

                foreach (var kvp in allLinkedServersOnThisServer)
                {
                    string serverLink = kvp.Key;
                    DataRow row = kvp.Value.row;
                    List<string> chain = kvp.Value.chain;
                    string provider = row["Provider"].ToString();
                    string localLogin = row["Local Login"] == DBNull.Value ? "" : row["Local Login"].ToString();

                    if (provider.StartsWith("SQLNCLI") || provider.StartsWith("MSOLEDBSQL"))
                        remoteSqlLinks.Add((serverLink, localLogin, row, chain));
                    else
                        remoteOtherLinks.Add($"{serverLink} ({provider})");
                }

                // Store non-SQL linked servers
                if (remoteOtherLinks.Count > 0)
                {
                    foreach (string link in remoteOtherLinks)
                        currentNode.NonSqlLinks.Add($"[OPENQUERY] {link}");
                }

                Logger.TraceNested($"Exploring SQL Server links on '{targetServer}' (found {remoteSqlLinks.Count})");

                // Explore each SQL Server link
                foreach (var (nextServer, nextLocalLogin, row, chainToReach) in remoteSqlLinks)
                {
                    // Skip if already explored globally
                    if (_globallyExploredServers.Contains(nextServer))
                    {
                        Logger.TraceNested($"Server '{nextServer}' already explored via another path. Skipping.");
                        continue;
                    }

                    HashSet<string> branchVisited = new(visitedInChain);
                    DatabaseContext branchContext = databaseContext.Duplicate();

                    // Apply the full impersonation chain needed to reach the right login context
                    if (chainToReach != null && chainToReach.Count > 0)
                    {
                        if (!TryApplyImpersonationChain(branchContext, chainToReach))
                        {
                            Logger.TraceNested($"Cannot apply impersonation chain on '{targetServer}' for link to '{nextServer}'. Skipping.");
                            continue;
                        }
                    }
                    else if (!string.IsNullOrEmpty(nextLocalLogin) && !nextLocalLogin.Equals(branchContext.Server.SystemUser, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!TryApplyImpersonationChain(branchContext, new List<string> { nextLocalLogin }))
                        {
                            Logger.TraceNested($"Cannot impersonate '{nextLocalLogin}' on '{targetServer}' for link to '{nextServer}'. Trying without impersonation.");
                        }
                    }

                    // Explore recursively
                    ExploreLinkedServer(branchContext, nextServer, currentNode, newPath, branchVisited, currentDepth + 1);
                }
            }
            catch (Exception ex)
            {
                Logger.TraceNested($"Failed to explore {targetServer}: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns all transitively reachable login chains from the current context,
        /// using the ImpersonationMap action silently. Each entry is an ordered list of
        /// logins to EXECUTE AS in sequence to reach the final login.
        /// </summary>
        private static List<List<string>> GetReachableLoginChains(DatabaseContext databaseContext)
        {
            var chains = new List<List<string>>();
            try
            {
                using (Logger.TemporarilySilent())
                {
                    var result = new ImpersonationMap().Execute(databaseContext) as DataTable;
                    if (result == null || result.Rows.Count == 0)
                        return chains;

                    foreach (DataRow row in result.Rows)
                    {
                        var chain = new List<string>();
                        string middleLogins = row["Middle Logins"]?.ToString() ?? "";
                        string endLogin = row["End Login"]?.ToString() ?? "";

                        if (!string.IsNullOrEmpty(middleLogins))
                        {
                            foreach (string login in middleLogins.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
                                chain.Add(login.Trim());
                        }

                        if (!string.IsNullOrEmpty(endLogin))
                            chain.Add(endLogin);

                        if (chain.Count > 0)
                            chains.Add(chain);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.TraceNested($"Failed to build impersonation chains: {ex.Message}");
            }
            return chains;
        }

        /// <summary>
        /// Applies a full multi-hop impersonation chain.
        /// For linked servers: updates the impersonation arrays so EXECUTE AS gets prepended to every query.
        /// For direct connections: issues individual EXECUTE AS commands that persist in the session.
        /// </summary>
        private static bool TryApplyImpersonationChain(DatabaseContext databaseContext, List<string> chain)
        {
            if (!databaseContext.QueryService.LinkedServers.IsEmpty)
            {
                // Linked servers: EXECUTE AS doesn't persist across separate EXEC() calls.
                // Instead, add logins to the impersonation arrays prepended to every query.
                string[] current = databaseContext.QueryService.ExecutionServer.ImpersonationUsers;
                var updated = new List<string>();
                if (current != null) updated.AddRange(current);
                updated.AddRange(chain);

                string[] updatedArray = updated.ToArray();
                databaseContext.QueryService.ExecutionServer.ImpersonationUsers = updatedArray;

                int lastServerIndex = databaseContext.QueryService.LinkedServers.ServerChain.Length - 1;
                if (lastServerIndex >= 0)
                {
                    databaseContext.QueryService.LinkedServers.ComputableImpersonationUsers[lastServerIndex] = updatedArray;
                }

                Logger.Trace($"Linked impersonation chain set to: [{string.Join(" -> ", updated)}]");
                return true;
            }

            // Direct connection: EXECUTE AS persists in the session
            int applied = 0;
            foreach (string login in chain)
            {
                try
                {
                    databaseContext.QueryService.ExecuteNonProcessing($"EXECUTE AS LOGIN = '{login.Replace("'", "''")}';");
                    applied++;
                }
                catch
                {
                    RevertChain(databaseContext, applied);
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Reverts a multi-hop impersonation chain.
        /// For linked servers: removes the last N logins from the impersonation arrays.
        /// For direct connections: issues REVERT once per hop.
        /// </summary>
        private static void RevertChain(DatabaseContext databaseContext, int hops)
        {
            if (!databaseContext.QueryService.LinkedServers.IsEmpty)
            {
                string[] current = databaseContext.QueryService.ExecutionServer.ImpersonationUsers;
                string[] restored = null;

                if (current != null && current.Length > hops)
                {
                    restored = current.Take(current.Length - hops).ToArray();
                }

                databaseContext.QueryService.ExecutionServer.ImpersonationUsers = restored;

                int lastServerIndex = databaseContext.QueryService.LinkedServers.ServerChain.Length - 1;
                if (lastServerIndex >= 0)
                {
                    databaseContext.QueryService.LinkedServers.ComputableImpersonationUsers[lastServerIndex] = restored ?? Array.Empty<string>();
                }

                Logger.Trace($"Linked impersonation chain restored to: [{(restored != null ? string.Join(" -> ", restored) : "none")}]");
                return;
            }

            // Direct connection: REVERT once per hop
            for (int i = 0; i < hops; i++)
                try { databaseContext.QueryService.ExecuteNonProcessing("REVERT;"); } catch { }
        }

        private int CountLeafNodes(ServerNode node)
        {
            if (node.Children.Count == 0)
                return 1;

            int count = 0;
            foreach (var child in node.Children)
            {
                count += CountLeafNodes(child);
            }
            return count;
        }

        /// <summary>
        /// Displays the linked server tree with ASCII art.
        /// </summary>
        private void DisplayTree()
        {
            // Root node - show privilege marker if sysadmin
            string rootPrivilege = _rootNode.IsSysadmin ? " ★" : "";
            Console.WriteLine($"{_rootNode.Alias} ({_rootNode.LoggedInUser} [{_rootNode.MappedUser}]){rootPrivilege}");

            if (_rootNode.NonSqlLinks.Count > 0)
            {
                Console.WriteLine($"    └── [OPENQUERY] {string.Join(", ", _rootNode.NonSqlLinks)}");
            }

            List<string> currentPath = new();
            for (int i = 0; i < _rootNode.Children.Count; i++)
            {
                bool isLast = (i == _rootNode.Children.Count - 1);
                DisplayTreeNode(_rootNode.Children[i], "", isLast, currentPath);
            }
        }

        private void DisplayTreeNode(ServerNode node, string indent, bool isLast, List<string> parentPath)
        {
            string connector = isLast ? "└── " : "├── ";
            string childIndent = indent + (isLast ? "    " : "│   ");

            // Build chain path for this node
            List<string> currentPath = new(parentPath);
            string chainPart = node.Alias;
            if (!string.IsNullOrEmpty(node.ImpersonatedUser))
            {
                chainPart = $"{node.Alias}/{node.ImpersonatedUser}";
            }
            currentPath.Add(chainPart);
            string chainCommand = string.Join(";", currentPath);

            // Format display name
            string displayName = node.Alias;
            if (!node.Alias.Equals(node.ActualName, StringComparison.OrdinalIgnoreCase))
            {
                displayName = $"{node.Alias} [{node.ActualName}]";
            }

            // Build role indicators
            string roleMarkers = "";
            if (node.IsSysadmin)
            {
                roleMarkers = " ★";
            }
            else if (node.ServerRoles.Count > 0)
            {
                roleMarkers = $" [{string.Join(", ", node.ServerRoles)}]";
            }

            // Build the main line: name (user [mapped]) -l chain
            Console.WriteLine($"{indent}{connector}{displayName} ({node.LoggedInUser} [{node.MappedUser}]){roleMarkers}  -l \"{chainCommand}\"");

            // Show non-SQL links if any
            if (node.NonSqlLinks.Count > 0)
            {
                Console.WriteLine($"{childIndent}└── [OPENQUERY] {string.Join(", ", node.NonSqlLinks)}");
            }

            // Display children
            for (int i = 0; i < node.Children.Count; i++)
            {
                bool childIsLast = (i == node.Children.Count - 1);
                DisplayTreeNode(node.Children[i], childIndent, childIsLast, currentPath);
            }
        }

        /// <summary>
        /// Displays chain commands for all discovered paths, grouped by privilege level.
        /// </summary>
        private void DisplayChainCommands()
        {
            var privilegedChains = new List<List<ServerNode>>();
            var standardChains = new List<List<ServerNode>>();

            foreach (var chain in _allChains)
            {
                if (chain.Count == 0) continue;

                var lastNode = chain[chain.Count - 1];
                if (lastNode.IsSysadmin)
                    privilegedChains.Add(chain);
                else
                    standardChains.Add(chain);
            }

            if (privilegedChains.Count > 0)
            {
                Logger.Success($"Privileged chains ({privilegedChains.Count}) - sysadmin at endpoint:");
                foreach (var chain in privilegedChains)
                {
                    DisplayChainCommand(chain);
                }
            }

            if (standardChains.Count > 0)
            {
                Logger.NewLine();
                Logger.Info($"Standard chains ({standardChains.Count}):");
                foreach (var chain in standardChains)
                {
                    DisplayChainCommand(chain);
                }
            }
        }

        private void DisplayChainCommand(List<ServerNode> chain)
        {
            if (chain.Count == 0) return;

            var serverList = new List<Server>();
            foreach (var node in chain)
            {
                serverList.Add(new Server
                {
                    Hostname = node.Alias,
                    ImpersonationUsers = string.IsNullOrEmpty(node.ImpersonatedUser) ? null : new[] { node.ImpersonatedUser },
                    Database = null
                });
            }

            var lastNode = chain[chain.Count - 1];
            string endpoint = lastNode.Alias;
            if (!lastNode.Alias.Equals(lastNode.ActualName, StringComparison.OrdinalIgnoreCase))
            {
                endpoint = $"{lastNode.Alias} [{lastNode.ActualName}]";
            }

            string userContext = $"({lastNode.LoggedInUser} [{lastNode.MappedUser}])";
            string roleInfo = "";
            if (lastNode.IsSysadmin)
            {
                roleInfo = " ★";
            }
            else if (lastNode.ServerRoles.Count > 0)
            {
                roleInfo = $" [{string.Join(", ", lastNode.ServerRoles)}]";
            }

            LinkedServers linkedServers = new LinkedServers(serverList.ToArray());
            string chainArg = linkedServers.GetChainArguments();

            Logger.InfoNested($"{endpoint} {userContext}{roleInfo}: -l \"{chainArg}\"");
        }

        /// <summary>
        /// Gets the server roles for the current user.
        /// </summary>
        private static List<string> GetUserServerRoles(DatabaseContext databaseContext)
        {
            string query = @"
SELECT name
FROM sys.server_principals
WHERE type = 'R' AND is_fixed_role = 1 AND name != 'public'
  AND IS_SRVROLEMEMBER(name) = 1;";

            var roles = new List<string>();
            try
            {
                DataTable rolesTable = databaseContext.QueryService.ExecuteTable(query);
                foreach (DataRow row in rolesTable.Rows)
                {
                    string roleName = row["name"].ToString();
                    // Skip sysadmin since it's shown with ★
                    if (!roleName.Equals("sysadmin", StringComparison.OrdinalIgnoreCase))
                    {
                        roles.Add(roleName);
                    }
                }
            }
            catch
            {
                // Role query failed, return empty list
            }
            return roles;
        }

        private static string BuildStateHash(string hostname, string mappedUser, string systemUser, bool isSysadmin)
        {
            // Use pipe delimiter to prevent collisions from adjacent string boundaries
            string stateString = $"{hostname?.ToUpperInvariant() ?? ""}|" +
                                $"{mappedUser?.ToUpperInvariant() ?? ""}|" +
                                $"{systemUser?.ToUpperInvariant() ?? ""}|" +
                                $"{isSysadmin}";

            return Misc.ComputeSHA256(stateString);
        }
    }
}
