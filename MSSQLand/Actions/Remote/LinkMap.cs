// MSSQLand/Actions/Remote/LinkMap.cs

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using MSSQLand.Actions.Database;
using MSSQLand.Models;
using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;

namespace MSSQLand.Actions.Remote
{
    /// <summary>
    /// Recursively explores all accessible linked server chains, mapping execution paths.
    /// Uses a tree structure for efficient storage and cleaner output.
    /// </summary>
    internal class LinkMap : BaseAction
    {
        /// <summary>
        /// Server roles that grant significant privileges beyond standard access.
        /// These get special highlighting in the tree and chain summary.
        /// </summary>
        private static readonly HashSet<string> ElevatedRoles = new(StringComparer.OrdinalIgnoreCase)
        {
            "securityadmin",  // Can grant permissions — near-sysadmin
            "serveradmin",    // Can change server configuration
            "setupadmin",     // Can add/remove linked servers
            "processadmin",   // Can kill processes
            "dbcreator",      // Can create/alter/drop databases
            "diskadmin",      // Can manage disk files
            "bulkadmin"       // Can run BULK INSERT
        };

        /// <summary>
        /// Represents a single impersonation step with the login name and its server roles.
        /// </summary>
        private class ImpersonationStep
        {
            public string Login { get; set; }
            public List<string> Roles { get; set; } = new();
            public bool IsSysadmin => Roles.Exists(r => r.Equals("sysadmin", StringComparison.OrdinalIgnoreCase));
            public bool IsElevated => !IsSysadmin && Roles.Exists(r => ElevatedRoles.Contains(r));

            public string PrivilegeMarker
            {
                get
                {
                    if (IsSysadmin) return " ★";
                    if (Roles.Count == 0) return "";
                    if (IsElevated) return $" ◆ [{string.Join(", ", Roles)}]";
                    return $" [{string.Join(", ", Roles)}]";
                }
            }
        }

        /// <summary>
        /// Represents a node in the linked server tree.
        /// </summary>
        private class ServerNode
        {
            public string Alias { get; set; }
            public string ActualName { get; set; }
            public string LoggedInUser { get; set; }
            public string MappedUser { get; set; }
            /// <summary>
            /// The impersonation chain used on the parent server to reach this linked server.
            /// Each step includes the login name and its server roles for visibility.
            /// </summary>
            public List<ImpersonationStep> ImpersonationChain { get; set; } = new();
            public bool IsSysadmin { get; set; }
            public List<string> ServerRoles { get; set; } = new();
            public List<string> NonSqlLinks { get; set; } = new();
            public List<ServerNode> Children { get; set; } = new();

            /// <summary>
            /// True if the user has any elevated (security-relevant) server roles beyond sysadmin.
            /// </summary>
            public bool IsElevated => !IsSysadmin && ServerRoles.Exists(r => ElevatedRoles.Contains(r));

            /// <summary>
            /// Returns the privilege marker for display: ★ sysadmin, ◆ elevated, roles list otherwise.
            /// Sysadmin implies all roles, so only shows ★ without listing them.
            /// </summary>
            public string PrivilegeMarker
            {
                get
                {
                    if (IsSysadmin) return " ★";
                    if (Roles.Count == 0) return "";
                    if (IsElevated) return $" ◆ [{string.Join(", ", Roles)}]";
                    return $" [{string.Join(", ", Roles)}]";
                }
            }

            /// <summary>
            /// Server roles excluding sysadmin (which is shown via ★).
            /// </summary>
            private List<string> Roles => ServerRoles.FindAll(r => !r.Equals("sysadmin", StringComparison.OrdinalIgnoreCase));
        }

        [ExcludeFromArguments]
        private ServerNode _rootNode;

        /// <summary>
        /// Global set of (server, login) pairs that have been fully explored.
        /// A server explored as one user may have different linked servers and permissions
        /// when accessed as a different user, so we track both.
        /// This prevents infinite loops while allowing re-exploration with different user contexts.
        /// </summary>
        [ExcludeFromArguments]
        private readonly HashSet<string> _globallyExploredContexts = new(StringComparer.OrdinalIgnoreCase);

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

            // Mark starting server+user as explored
            _globallyExploredContexts.Add($"{databaseContext.Server.Hostname}|{databaseContext.Server.SystemUser}".ToUpperInvariant());

            // Compute starting server's state hash for loop detection
            string startingHash = BuildStateHash(
                databaseContext.Server.Hostname,
                _rootNode.MappedUser,
                _rootNode.LoggedInUser,
                _rootNode.IsSysadmin
            );

            // Build complete map of reachable SQL links across all transitive impersonation chains.
            // Key: (server name, local login) — same server with different login mappings are separate entries.
            var reachableChains = GetReachableLoginChains(databaseContext);
            var allSqlLinks = new Dictionary<(string server, string localLogin), (DataRow row, List<string> chain)>();

            // Current user's links (already filtered to SQL above)
            foreach (DataRow row in sqlServerLinks)
            {
                string link = row["Link"].ToString();
                string localLogin = row["Local Login"] == DBNull.Value ? "" : row["Local Login"].ToString();
                var key = (link.ToUpperInvariant(), localLogin.ToUpperInvariant());
                if (!allSqlLinks.ContainsKey(key))
                    allSqlLinks[key] = (row, null);
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
                        string localLogin = row["Local Login"] == DBNull.Value ? "" : row["Local Login"].ToString();
                        var key = (link.ToUpperInvariant(), localLogin.ToUpperInvariant());
                        if (!allSqlLinks.ContainsKey(key))
                            allSqlLinks[key] = (row, chain);
                    }
                }
                finally
                {
                    RevertChain(databaseContext, chain.Count);
                }
            }

            Logger.TaskNested($"Total reachable SQL Server linked servers: {allSqlLinks.Count}");

            Stopwatch totalStopwatch = Stopwatch.StartNew();

            // Explore each discovered SQL link using the SAME connection
            foreach (var kvp in allSqlLinks)
            {
                string remoteServer = kvp.Key.server;
                var (linkRow, chainToReach) = kvp.Value;
                string requiredLogin = linkRow["Local Login"] == DBNull.Value ? "" : linkRow["Local Login"].ToString();

                if (_globallyExploredContexts.Contains($"{remoteServer}|{requiredLogin}".ToUpperInvariant()))
                {
                    Logger.TraceNested($"Server '{remoteServer}' already explored as '{requiredLogin}'. Skipping.");
                    continue;
                }

                Stopwatch serverStopwatch = Stopwatch.StartNew();

                HashSet<string> visitedInChain = new() { startingHash };
                List<ServerNode> currentPath = new();

                // Apply session-level impersonation (EXECUTE AS on the direct connection)
                // At this point LinkedServers is empty, so TryApplyImpersonationChain uses EXECUTE AS mode
                int sessionHops = 0;
                if (chainToReach != null && chainToReach.Count > 0)
                {
                    if (!TryApplyImpersonationChain(databaseContext, chainToReach))
                    {
                        Logger.TraceNested($"Cannot apply impersonation chain for link to '{remoteServer}'. Skipping.");
                        continue;
                    }
                    sessionHops = chainToReach.Count;
                }
                else if (!string.IsNullOrEmpty(requiredLogin) && !requiredLogin.Equals(databaseContext.Server.SystemUser, StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryApplyImpersonationChain(databaseContext, new List<string> { requiredLogin }))
                    {
                        Logger.TraceNested($"Cannot impersonate '{requiredLogin}' on starting server. Skipping link to '{remoteServer}'.");
                        continue;
                    }
                    sessionHops = 1;
                }

                try
                {
                    // Determine the impersonation chain used on the starting server to reach this link
                    List<string> startingImpersonationLogins = chainToReach ?? (!string.IsNullOrEmpty(requiredLogin) && !requiredLogin.Equals(databaseContext.Server.SystemUser, StringComparison.OrdinalIgnoreCase)
                        ? new List<string> { requiredLogin }
                        : new List<string>());

                    // Build impersonation steps with roles (chain is already applied at this point)
                    List<ImpersonationStep> startingImpersonation = BuildImpersonationSteps(databaseContext, startingImpersonationLogins);

                    ExploreLinkedServer(databaseContext, remoteServer, _rootNode, currentPath, visitedInChain, startingImpersonation, currentDepth: 0);
                }
                finally
                {
                    // Revert session-level impersonation (LinkedServers is empty again after ExploreLinkedServer)
                    if (sessionHops > 0)
                        RevertChain(databaseContext, sessionHops);
                }

                serverStopwatch.Stop();
                Logger.TraceNested($"Explored '{remoteServer}' in {serverStopwatch.Elapsed.TotalSeconds:F2}s");
            }

            totalStopwatch.Stop();

            // Count total chains
            int totalChains = CountLeafNodes(_rootNode);

            if (totalChains == 0)
            {
                Logger.Warning("No accessible linked server chains found.");
                return null;
            }

            Logger.NewLine();
            Logger.Success($"Found {totalChains} accessible chain(s) in {totalStopwatch.Elapsed.TotalSeconds:F2}s");

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
        /// Uses a single connection with AddToChain/RemoveLastFromChain for clean push/pop semantics,
        /// and EXECUTE AS arrays for linked server impersonation (same pattern as ImpersonationMap).
        /// </summary>
        private void ExploreLinkedServer(DatabaseContext databaseContext, string targetServer,
            ServerNode parentNode, List<ServerNode> currentPath, HashSet<string> visitedInChain,
            List<ImpersonationStep> parentImpersonationChain, int currentDepth)
        {
            if (currentDepth >= _limit)
            {
                Logger.TraceNested($"Limit {_limit} reached at server '{targetServer}'. Backtracking.");
                return;
            }

            // Push this server onto the linked chain — popped in finally
            databaseContext.QueryService.LinkedServers.AddToChain(targetServer);

            try
            {
                // Clear stale caches since we changed execution context
                databaseContext.UserService.ClearCaches();

                // Silently probe connectivity — avoids noisy error output for inaccessible servers
                string actualServerName = targetServer;
                string mappedUser, remoteLoggedInUser;
                try
                {
                    using (Logger.TemporarilySilent())
                    {
                        try
                        {
                            string serverNameResult = databaseContext.QueryService.ExecuteScalar("SELECT @@SERVERNAME")?.ToString();
                            if (!string.IsNullOrEmpty(serverNameResult))
                                actualServerName = serverNameResult;
                        }
                        catch
                        {
                            // Keep alias as fallback
                        }

                        (mappedUser, remoteLoggedInUser) = databaseContext.UserService.GetInfo();
                    }
                }
                catch (Exception ex)
                {
                    Logger.TraceNested($"Failed to explore {targetServer}: {ex.Message}");
                    return;
                }

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
                    ImpersonationChain = parentImpersonationChain ?? new List<ImpersonationStep>(),
                    IsSysadmin = isSysadmin,
                    ServerRoles = GetUserServerRoles(databaseContext)
                };

                // Add to parent's children
                parentNode.Children.Add(currentNode);

                // Track the path for chain commands
                var newPath = new List<ServerNode>(currentPath) { currentNode };
                _allChains.Add(newPath);

                // Mark this server+user as globally explored
                _globallyExploredContexts.Add($"{targetServer}|{remoteLoggedInUser}".ToUpperInvariant());

                // Build complete map of reachable links via all transitive impersonation chains.
                // Key: (server name, local login) — same server with different login mappings are separate entries.
                var remoteReachableChains = GetReachableLoginChains(databaseContext);
                Logger.TraceNested($"Reachable login chains on '{targetServer}': {remoteReachableChains.Count}");

                var allLinkedServersOnThisServer = new Dictionary<(string server, string localLogin), (DataRow row, List<string> chain)>();

                // Current user's links
                try
                {
                    DataTable currentUserLinks = Links.GetLinkedServers(databaseContext);
                    foreach (DataRow row in currentUserLinks.Rows)
                    {
                        string serverLink = row["Link"].ToString();
                        string localLogin = row["Local Login"] == DBNull.Value ? "" : row["Local Login"].ToString();
                        var key = (serverLink.ToUpperInvariant(), localLogin.ToUpperInvariant());
                        if (!allLinkedServersOnThisServer.ContainsKey(key))
                            allLinkedServersOnThisServer[key] = (row, null);
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
                            string localLogin = row["Local Login"] == DBNull.Value ? "" : row["Local Login"].ToString();
                            var key = (serverLink.ToUpperInvariant(), localLogin.ToUpperInvariant());
                            if (!allLinkedServersOnThisServer.ContainsKey(key))
                                allLinkedServersOnThisServer[key] = (row, chain);
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
                    string serverLink = kvp.Key.server;
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
                    // Skip if already explored with the same user context
                    string remoteLogin = nextLocalLogin;
                    // If we don't know the remote login yet, we can't pre-filter — explore and let the hash check handle it
                    if (!string.IsNullOrEmpty(remoteLogin) && _globallyExploredContexts.Contains($"{nextServer}|{remoteLogin}".ToUpperInvariant()))
                    {
                        Logger.TraceNested($"Server '{nextServer}' already explored as '{remoteLogin}'. Skipping.");
                        continue;
                    }

                    HashSet<string> branchVisited = new(visitedInChain);

                    // Apply impersonation chain on this linked server for the child link
                    // Since LinkedServers is not empty, TryApplyImpersonationChain uses array mode
                    int impersonationHops = 0;
                    if (chainToReach != null && chainToReach.Count > 0)
                    {
                        if (!TryApplyImpersonationChain(databaseContext, chainToReach))
                        {
                            Logger.TraceNested($"Cannot apply impersonation chain on '{targetServer}' for link to '{nextServer}'. Skipping.");
                            continue;
                        }
                        impersonationHops = chainToReach.Count;
                    }
                    else if (!string.IsNullOrEmpty(nextLocalLogin) && !nextLocalLogin.Equals(remoteLoggedInUser, StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryApplyImpersonationChain(databaseContext, new List<string> { nextLocalLogin }))
                        {
                            impersonationHops = 1;
                        }
                        else
                        {
                            Logger.TraceNested($"Cannot impersonate '{nextLocalLogin}' on '{targetServer}' for link to '{nextServer}'. Trying without impersonation.");
                        }
                    }

                    try
                    {
                        // Determine the impersonation chain used on this server to reach the next link
                        List<string> nextImpersonationLogins = chainToReach ?? (!string.IsNullOrEmpty(nextLocalLogin) && !nextLocalLogin.Equals(remoteLoggedInUser, StringComparison.OrdinalIgnoreCase)
                            ? new List<string> { nextLocalLogin }
                            : new List<string>());

                        // Build impersonation steps with roles (chain is already applied at this point)
                        List<ImpersonationStep> nextImpersonation = BuildImpersonationSteps(databaseContext, nextImpersonationLogins);

                        // Recurse — ExploreLinkedServer will AddToChain/RemoveLastFromChain internally
                        ExploreLinkedServer(databaseContext, nextServer, currentNode, newPath, branchVisited, nextImpersonation, currentDepth + 1);
                    }
                    finally
                    {
                        // Revert impersonation on this server's linked array slot
                        if (impersonationHops > 0)
                            RevertChain(databaseContext, impersonationHops);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.TraceNested($"Failed to explore {targetServer}: {ex.Message}");
            }
            finally
            {
                // Pop this server from the linked chain and clear stale caches
                databaseContext.QueryService.LinkedServers.RemoveLastFromChain();
                databaseContext.UserService.ClearCaches();
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
        /// Delegates to UserService which handles both direct and linked server modes.
        /// </summary>
        private static bool TryApplyImpersonationChain(DatabaseContext databaseContext, List<string> chain)
        {
            int applied = 0;
            foreach (string login in chain)
            {
                if (databaseContext.UserService.TryImpersonateUser(login))
                {
                    applied++;
                }
                else
                {
                    RevertChain(databaseContext, applied);
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Reverts a multi-hop impersonation chain by reverting one hop at a time.
        /// Delegates to UserService which handles both direct and linked server modes.
        /// </summary>
        private static void RevertChain(DatabaseContext databaseContext, int hops)
        {
            for (int i = 0; i < hops; i++)
            {
                try { databaseContext.UserService.RevertImpersonation(); } catch { }
            }
        }

        /// <summary>
        /// Builds ImpersonationStep list with roles for each login in the chain.
        /// The impersonation chain must already be applied (via TryApplyImpersonationChain)
        /// so we can query roles at the final state. For efficiency, we query roles once
        /// at the end of the chain rather than at each step — the last user's roles are most relevant.
        /// For intermediate users, we apply step-by-step and query roles at each step.
        /// </summary>
        private static List<ImpersonationStep> BuildImpersonationSteps(DatabaseContext databaseContext, List<string> logins)
        {
            if (logins == null || logins.Count == 0)
                return new List<ImpersonationStep>();

            var steps = new List<ImpersonationStep>();

            // The full chain is already applied. To get roles at each step,
            // revert all, then re-apply one by one, querying roles at each step.
            int total = logins.Count;
            RevertChain(databaseContext, total);

            for (int i = 0; i < total; i++)
            {
                TryApplyImpersonationChain(databaseContext, new List<string> { logins[i] });

                var step = new ImpersonationStep { Login = logins[i] };
                try
                {
                    step.Roles = GetUserServerRoles(databaseContext);
                }
                catch
                {
                    // Roles query failed, leave empty
                }
                steps.Add(step);
            }

            // Chain is now fully re-applied
            return steps;
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
            // Root node - show privilege marker
            Console.WriteLine($"{_rootNode.Alias} ({_rootNode.LoggedInUser} [{_rootNode.MappedUser}]){_rootNode.PrivilegeMarker}");

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
            // Build chain path for this node, including impersonation on the parent server
            List<string> currentPath = new(parentPath);
            string chainPart = node.Alias;
            List<string> impLogins = node.ImpersonationChain.ConvertAll(s => s.Login);

            if (impLogins.Count > 0)
            {
                if (currentPath.Count > 0)
                {
                    string lastPart = currentPath[currentPath.Count - 1];
                    currentPath[currentPath.Count - 1] = lastPart + "/" + string.Join("/", impLogins);
                }
                else
                {
                    chainPart = $"({string.Join(" → ", impLogins)}) {node.Alias}";
                }
            }
            currentPath.Add(chainPart);
            string chainCommand = string.Join(";", currentPath);

            // Format display name
            string displayName = node.Alias;
            if (!node.Alias.Equals(node.ActualName, StringComparison.OrdinalIgnoreCase))
            {
                displayName = $"{node.Alias} [{node.ActualName}]";
            }

            bool hasChildren = node.Children.Count > 0 || node.NonSqlLinks.Count > 0;

            if (node.ImpersonationChain.Count > 0)
            {
                // Render impersonation steps as intermediate tree nodes
                string currentIndent = indent;
                for (int s = 0; s < node.ImpersonationChain.Count; s++)
                {
                    var step = node.ImpersonationChain[s];

                    string stepConnector;
                    string stepChildIndent;
                    if (s == 0)
                    {
                        stepConnector = isLast ? "└── " : "├── ";
                        stepChildIndent = currentIndent + (isLast ? "    " : "│   ");
                    }
                    else
                    {
                        stepConnector = "└── ";
                        stepChildIndent = currentIndent + "    ";
                    }

                    Console.WriteLine($"{currentIndent}{stepConnector}{step.Login}{step.PrivilegeMarker}");
                    currentIndent = stepChildIndent;
                }

                // After all impersonation steps, render the linked server node
                string serverChildIndent = currentIndent + "    ";
                Console.WriteLine($"{currentIndent}╚══ {displayName} ({node.LoggedInUser} [{node.MappedUser}]){node.PrivilegeMarker}");

                if (node.NonSqlLinks.Count > 0)
                {
                    Console.WriteLine($"{serverChildIndent}└── [OPENQUERY] {string.Join(", ", node.NonSqlLinks)}");
                }

                for (int i = 0; i < node.Children.Count; i++)
                {
                    bool childIsLast = (i == node.Children.Count - 1);
                    DisplayTreeNode(node.Children[i], serverChildIndent, childIsLast, currentPath);
                }
            }
            else
            {
                // No impersonation — render directly
                string connector = isLast ? "╚══ " : "╠══ ";
                string childIndent = indent + (isLast ? "    " : "║   ");

                Console.WriteLine($"{indent}{connector}{displayName} ({node.LoggedInUser} [{node.MappedUser}]){node.PrivilegeMarker}");

                if (node.NonSqlLinks.Count > 0)
                {
                    Console.WriteLine($"{childIndent}└── [OPENQUERY] {string.Join(", ", node.NonSqlLinks)}");
                }

                for (int i = 0; i < node.Children.Count; i++)
                {
                    bool childIsLast = (i == node.Children.Count - 1);
                    DisplayTreeNode(node.Children[i], childIndent, childIsLast, currentPath);
                }
            }
        }

        /// <summary>
        /// Builds a summary DataTable of all discovered chains and displays them grouped by privilege.
        /// </summary>
        private DataTable DisplayChainCommands()
        {
            DataTable result = new DataTable();
            result.Columns.Add("Endpoint", typeof(string));
            result.Columns.Add("Login", typeof(string));
            result.Columns.Add("Mapped To", typeof(string));
            result.Columns.Add("Server Roles", typeof(string));
            result.Columns.Add("Command", typeof(string));

            foreach (var chain in _allChains.OrderByDescending(c => GetChainPriority(c)))
            {
                if (chain.Count == 0) continue;
                var row = BuildChainRow(chain);
                result.Rows.Add(row);
            }

            if (result.Rows.Count > 0)
            {
                Console.WriteLine(OutputFormatter.ConvertDataTable(result));
            }

            return result;
        }

        /// <summary>
        /// Returns a sort priority: 2 = privileged (sysadmin), 1 = elevated, 0 = standard.
        /// </summary>
        private static int GetChainPriority(List<ServerNode> chain)
        {
            if (chain.Count == 0) return 0;
            var lastNode = chain[chain.Count - 1];
            if (lastNode.IsSysadmin) return 2;
            if (lastNode.IsElevated) return 1;
            return 0;
        }

        /// <summary>
        /// Builds a DataRow array for a single chain, showing the real MSSQLand command.
        /// </summary>
        private object[] BuildChainRow(List<ServerNode> chain)
        {
            var lastNode = chain[chain.Count - 1];

            // Build linked server list for -l argument
            var serverList = new List<Server>();
            for (int i = 0; i < chain.Count; i++)
            {
                var node = chain[i];

                if (i > 0 && node.ImpersonationChain.Count > 0)
                {
                    serverList[serverList.Count - 1].ImpersonationUsers = node.ImpersonationChain.ConvertAll(s => s.Login).ToArray();
                }

                serverList.Add(new Server
                {
                    Hostname = node.Alias,
                    ImpersonationUsers = null,
                    Database = null
                });
            }

            LinkedServers linkedServers = new LinkedServers(serverList.ToArray());
            string chainArg = linkedServers.GetChainArguments();

            // Build host argument with starting server impersonation
            string hostArg = Misc.BracketIdentifier(_rootNode.Alias);
            if (chain.Count > 0 && chain[0].ImpersonationChain.Count > 0)
            {
                hostArg += "/" + string.Join("/", chain[0].ImpersonationChain.ConvertAll(s => s.Login));
            }

            // Full command
            string command = $"\"{hostArg}\" -l \"{chainArg}\"";

            // Endpoint display
            string endpoint = lastNode.Alias;
            if (!lastNode.Alias.Equals(lastNode.ActualName, StringComparison.OrdinalIgnoreCase))
            {
                endpoint = $"{lastNode.Alias} [{lastNode.ActualName}]";
            }

            // Login context
            string login = lastNode.LoggedInUser;
            string mappedTo = lastNode.MappedUser;

            // Privilege level
            string privilege;
            if (lastNode.IsSysadmin)
                privilege = "sysadmin";
            else if (lastNode.IsElevated)
                privilege = string.Join(", ", lastNode.ServerRoles.FindAll(r => ElevatedRoles.Contains(r)));
            else
                privilege = "";

            return new object[] { endpoint, login, mappedTo, privilege, command };
        }

        /// <summary>
        /// Gets all server roles (fixed and custom) the current user is a member of.
        /// </summary>
        private static List<string> GetUserServerRoles(DatabaseContext databaseContext)
        {
            string query = @"
SELECT name
FROM sys.server_principals
WHERE type = 'R'
  AND name != 'public'
  AND name NOT LIKE '##%##'
  AND ISNULL(IS_SRVROLEMEMBER(name), 0) = 1
ORDER BY name;";

            var roles = new List<string>();
            try
            {
                DataTable rolesTable = databaseContext.QueryService.ExecuteTable(query);
                foreach (DataRow row in rolesTable.Rows)
                {
                    roles.Add(row["name"].ToString());
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
