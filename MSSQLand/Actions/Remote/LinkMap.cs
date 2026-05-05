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
            "securityadmin",  // Can grant permissions: near-sysadmin
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
            /// Privilege escalation paths discovered via impersonation at this server.
            /// Each path is a chain of impersonation steps ending in a privileged user (sysadmin/elevated).
            /// </summary>
            public List<List<ImpersonationStep>> EscalationPaths { get; set; } = new();

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
        /// Negative cache of (sourceServer, targetServer, callerLogin) link-attempt failures.
        /// Distinct from <see cref="_globallyExploredContexts"/>, which records *successful*
        /// explorations. SQL Server evaluates linked-server mappings only at the immediate source,
        /// so a rejection observed for (source, target, caller) is stable for that exact tuple
        /// and can safely short-circuit later attempts that would land at the same source as the
        /// same caller. Keyed by the immediate source only (not the upstream path) to avoid false
        /// positives caused by upstream context.
        /// </summary>
        [ExcludeFromArguments]
        private readonly HashSet<string> _failedLinkAttempts = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Builds the canonical key for the global "already explored" set: (server, login).
        /// </summary>
        private static string ContextKey(string server, string login)
            => $"{server}|{login}".ToUpperInvariant();

        /// <summary>
        /// Builds the canonical key for the negative link-attempt cache:
        /// (sourceServer, targetServer, callerLogin).
        /// </summary>
        private static string LinkAttemptKey(string sourceServer, string targetServer, string callerLogin)
            => $"{sourceServer}|{targetServer}|{callerLogin}".ToUpperInvariant();

        /// <summary>
        /// Reads a string column from a DataRow, returning empty string for DBNull.
        /// Centralizes the repeated nullability check on linked-server metadata columns.
        /// </summary>
        private static string GetRowString(DataRow row, string column)
            => row[column] == DBNull.Value ? "" : row[column].ToString();

        /// <summary>
        /// Formats a parent impersonation chain for log messages.
        /// Returns "login1 -> login2" or the provided fallback login when the chain is empty.
        /// </summary>
        private static string FormatImpersonationContext(List<ImpersonationStep> chain, string fallbackLogin = "current login")
            => chain != null && chain.Count > 0
                ? string.Join(" -> ", chain.Select(s => s.Login))
                : fallbackLogin;

        /// <summary>
        /// Tracks all discovered chains for programmatic access.
        /// </summary>
        [ExcludeFromArguments]
        private readonly List<List<ServerNode>> _allChains = new();

        [ArgumentMetadata(Position = 0, Description = "Maximum recursion depth (default: 5, max: 15)")]
        private int _limit = 5;

        /// <summary>
        /// Impersonation users applied on the starting server before exploration begins.
        /// Captured from databaseContext.Server.ImpersonationUsers at the start of Execute().
        /// Included in the host argument of generated commands.
        /// </summary>
        [ExcludeFromArguments]
        private string[] _startingImpersonation = Array.Empty<string>();

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

            // Capture the initial impersonation so commands reflect the full execution context
            _startingImpersonation = databaseContext.Server.ImpersonationUsers ?? Array.Empty<string>();

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

            // Separate SQL Server links (chainable) from others (queryable only).
            // Track "No visibility" rows separately so we can report them and try impersonation.
            var sqlServerLinks = new List<DataRow>();
            var otherLinks = new List<DataRow>();
            var noVisibilityLinks = new List<DataRow>();

            foreach (DataRow row in allLinkedServers.Rows)
            {
                if (GetRowString(row, "Access") == "No visibility")
                {
                    noVisibilityLinks.Add(row);
                    continue;
                }

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

            if (noVisibilityLinks.Count > 0)
            {
                Logger.Trace($"Linked servers with no visibility into linked_logins (current user): {noVisibilityLinks.Count}");
                foreach (DataRow row in noVisibilityLinks)
                {
                    Logger.TraceNested($"{row["Link"]} ({row["Provider"]}) - will retry under impersonation");
                }
            }

            Logger.Trace($"SQL Server linked servers (chainable): {sqlServerLinks.Count}");
            foreach (DataRow row in sqlServerLinks)
            {
                string link = row["Link"].ToString();
                string localLogin = GetRowString(row, "Local Login");
                string remoteLogin = GetRowString(row, "Remote Login");
                string access = GetRowString(row, "Access");

                string description = !string.IsNullOrEmpty(localLogin)
                    ? $"{localLogin} [{access}]" + (!string.IsNullOrEmpty(remoteLogin) ? $" → {remoteLogin}" : "")
                    : access + (!string.IsNullOrEmpty(remoteLogin) ? $" → {remoteLogin}" : "");

                Logger.TraceNested($"{link} [{description}]");
            }

            // Create root node representing the starting server.
            // Note: GetServerRoles is called first so it populates the admin-status cache;
            // IsAdmin then resolves from cache, avoiding a separate round-trip.
            List<string> rootRoles = databaseContext.UserService.GetServerRoles();
            _rootNode = new ServerNode
            {
                Alias = databaseContext.Server.Hostname,
                ActualName = databaseContext.Server.Hostname,
                LoggedInUser = databaseContext.Server.SystemUser,
                MappedUser = databaseContext.Server.MappedUser,
                IsSysadmin = databaseContext.UserService.IsAdmin(),
                ServerRoles = rootRoles
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
                Logger.Trace($"Other linked servers (queryable via OPENQUERY):");
                foreach (DataRow row in otherLinks)
                {
                    string name = row["Link"].ToString();
                    string provider = row["Provider"].ToString();
                    string product = row["Product"].ToString();
                    Logger.TraceNested($"{name} ({provider}) - {product}");
                }
            }

            if (sqlServerLinks.Count == 0 && noVisibilityLinks.Count == 0)
            {
                Logger.Warning("No SQL Server linked servers to explore.");
                return null;
            }

            if (sqlServerLinks.Count == 0 && noVisibilityLinks.Count > 0)
            {
                Logger.Warning($"Current user has no visibility into any linked server mappings. Will attempt impersonation to gain visibility.");
            }

            // Mark starting server+user as explored
            _globallyExploredContexts.Add(ContextKey(databaseContext.Server.Hostname, databaseContext.Server.SystemUser));

            // Compute starting server's state hash for loop detection
            string startingHash = databaseContext.ComputeStateHash();

            // Build complete map of reachable SQL links across all transitive impersonation chains.
            // Key: (server name, local login): same server with different login mappings are separate entries.
            // Skip impersonation discovery if already sysadmin: we can already see all linked servers.
            // Force impersonation discovery when current user has zero direct visibility (all rows were
            // "No visibility") — an impersonable user may see what we cannot.
            bool forceImpersonationDiscovery = sqlServerLinks.Count == 0 && noVisibilityLinks.Count > 0;
            var reachableChains = _rootNode.IsSysadmin
                ? new List<List<string>>()
                : GetReachableLoginChains(databaseContext);

            if (reachableChains.Count > 0)
            {
                Logger.Trace($"Reachable login chains from current user: {reachableChains.Count}");
                foreach (var chain in reachableChains)
                    Logger.TraceNested($"[{string.Join(" -> ", chain)}]");
            }
            else if (forceImpersonationDiscovery)
            {
                Logger.Warning("No impersonable logins found. Cannot gain visibility into linked server mappings.");
                return null;
            }

            var allSqlLinks = new Dictionary<(string server, string localLogin), (DataRow row, List<string> chain)>();

            // Current user's links (already filtered to SQL above)
            foreach (DataRow row in sqlServerLinks)
            {
                string link = row["Link"].ToString();
                string localLogin = GetRowString(row, "Local Login");
                var key = (link.ToUpperInvariant(), localLogin.ToUpperInvariant());
                if (!allSqlLinks.ContainsKey(key))
                    allSqlLinks[key] = (row, null);
            }

            // Additional links visible from each transitive impersonation chain
            foreach (var chain in reachableChains)
            {
                // Skip chains ending at system accounts: they add no unique linked server info and cause errors
                if (UserService.IsSystemAccount(chain[chain.Count - 1]))
                    continue;

                if (!TryApplyImpersonationChain(databaseContext, chain))
                    continue;
                try
                {
                    DataTable chainLinks = Links.GetLinkedServers(databaseContext);
                    int gained = 0;
                    foreach (DataRow row in chainLinks.Rows)
                    {
                        // Skip rows where the impersonated user also has no visibility
                        if (GetRowString(row, "Access") == "No visibility")
                            continue;

                        string provider = row["Provider"].ToString();
                        if (!provider.StartsWith("SQLNCLI") && !provider.StartsWith("MSOLEDBSQL"))
                            continue;
                        string link = row["Link"].ToString();
                        string localLogin = GetRowString(row, "Local Login");
                        var key = (link.ToUpperInvariant(), localLogin.ToUpperInvariant());
                        if (!allSqlLinks.ContainsKey(key))
                        {
                            allSqlLinks[key] = (row, chain);
                            gained++;
                        }
                    }
                    if (gained > 0)
                        Logger.TraceNested($"Gained visibility into {gained} link mapping(s) via [{string.Join(" -> ", chain)}]");
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Failed to query linked servers via chain [{string.Join(" -> ", chain)}]: {ex.Message}");
                }
                finally
                {
                    RevertChain(databaseContext, chain.Count);
                }
            }

            // For servers visible to the current user (chain=null) that have an explicit remote login
            // (catch-all mapped to a fixed remote credential), also create per-chain entries so the
            // exploration loop tries connecting while impersonated.
            // Skip entries with empty Remote Login (Windows pass-through): these rely on caller
            // Windows identity and will always fail for SQL logins like operator/john/etc.
            var existingKeys = allSqlLinks.Keys.ToList();
            foreach (var chain in reachableChains)
            {
                if (UserService.IsSystemAccount(chain[chain.Count - 1]))
                    continue;

                string chainEndLogin = chain[chain.Count - 1];
                foreach (var key in existingKeys)
                {
                    if (allSqlLinks[key].chain != null)
                        continue; // Already has a chain, skip

                    DataRow existingRow = allSqlLinks[key].row;
                    string rowRemoteLogin = GetRowString(existingRow, "Remote Login");
                    if (string.IsNullOrEmpty(rowRemoteLogin))
                        continue; // Windows pass-through: won't work for SQL logins

                    var chainKey = (key.server, chainEndLogin.ToUpperInvariant());
                    if (!allSqlLinks.ContainsKey(chainKey))
                        allSqlLinks[chainKey] = (existingRow, chain);
                }
            }

            Logger.TaskNested($"Total reachable SQL Server linked servers: {allSqlLinks.Count}");

            Stopwatch totalStopwatch = Stopwatch.StartNew();

            // Explore each discovered SQL link using the SAME connection
            foreach (var kvp in allSqlLinks)
            {
                string remoteServer = kvp.Key.server;
                var (linkRow, chainToReach) = kvp.Value;
                string requiredLogin = GetRowString(linkRow, "Local Login");

                if (_globallyExploredContexts.Contains(ContextKey(remoteServer, requiredLogin)))
                {
                    Logger.TraceNested($"Server '{remoteServer}' already explored as '{requiredLogin}'. Skipping.");
                    continue;
                }

                // Negative cache: skip if this exact link from the root has already failed for this caller
                string rootCallerLogin = chainToReach != null && chainToReach.Count > 0
                    ? chainToReach[chainToReach.Count - 1]
                    : (!string.IsNullOrEmpty(requiredLogin) ? requiredLogin : databaseContext.Server.SystemUser);
                if (_failedLinkAttempts.Contains(LinkAttemptKey(_rootNode.Alias, remoteServer, rootCallerLogin)))
                {
                    Logger.TraceNested($"Skipping '{remoteServer}': previous attempt from '{_rootNode.Alias}' as '{rootCallerLogin}' was rejected.");
                    continue;
                }

                // Skip self-mapping entries (empty Local Login) when no impersonation chain is available.
                // An empty Local Login means local_principal_id=0 ("use current credentials"), but if
                // the remote server has no login for the current user the connection will fail.
                // Chain-based entries derived from this row (keyed by chain-end login) are unaffected.
                if (string.IsNullOrEmpty(requiredLogin) && chainToReach == null)
                {
                    Logger.TraceNested($"Skipping '{remoteServer}': no explicit local login mapping and no impersonation chain available.");
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

                    List<ImpersonationStep> startingImpersonation = BuildImpersonationSteps(startingImpersonationLogins);

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

            // Count total chains.
            // Guard against the root having no children: CountLeafNodes returns 1 for any leaf node,
            // including the root itself when all explorations failed (wrong — should be 0).
            int totalChains = _rootNode.Children.Count == 0 ? 0 : CountLeafNodes(_rootNode);
            int totalEscalations = CountEscalationPaths(_rootNode);

            if (totalChains == 0 && totalEscalations == 0)
            {
                Logger.Warning("No accessible linked server chains found.");
                return null;
            }

            Logger.NewLine();
            string summary = $"Found {totalChains} accessible chain(s)";
            if (totalEscalations > 0)
                summary += $" and {totalEscalations} privilege escalation path(s)";
            summary += $" in {totalStopwatch.Elapsed.TotalSeconds:F2}s";
            Logger.Success(summary);

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

            // Push this server onto the linked chain: popped in finally
            databaseContext.QueryService.LinkedServers.AddToChain(targetServer);

            try
            {
                // Clear stale caches since we changed execution context
                databaseContext.UserService.ClearCaches();

                // Silently probe connectivity: avoids noisy error output for inaccessible servers
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
                    string asWho = FormatImpersonationContext(parentImpersonationChain, parentNode.LoggedInUser);
                    Logger.TraceNested($"Failed to explore {targetServer} as [{asWho}]: {ex.Message}");

                    // Record the rejection in the negative cache so sibling chains landing at the
                    // same parent with the same effective caller don't re-attempt this dead edge.
                    string parentCallerLogin = parentImpersonationChain != null && parentImpersonationChain.Count > 0
                        ? parentImpersonationChain[parentImpersonationChain.Count - 1].Login
                        : parentNode.LoggedInUser;
                    _failedLinkAttempts.Add(LinkAttemptKey(parentNode.Alias, targetServer, parentCallerLogin));
                    return;
                }

                Logger.TraceNested($"Logged in to server '{targetServer}' (actual: {actualServerName}) as: '{remoteLoggedInUser}' [{mappedUser}]");

                // Fetch roles first so the admin-status cache is populated as a side effect;
                // the subsequent IsAdmin() then resolves from cache without a round-trip.
                List<string> nodeRoles = databaseContext.UserService.GetServerRoles();
                bool isSysadmin = databaseContext.UserService.IsAdmin();
                string stateHash = Server.ComputeExplorationHash(targetServer, mappedUser, remoteLoggedInUser, isSysadmin);

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
                    ServerRoles = nodeRoles
                };

                // Add to parent's children
                parentNode.Children.Add(currentNode);

                // Track the path for chain commands
                var newPath = new List<ServerNode>(currentPath) { currentNode };
                _allChains.Add(newPath);

                // Mark this server+user as globally explored.
                // HashSet.Add returns false when the element was already present, meaning we have
                // already fully mapped this (server, user) pair via a different chain path.
                // Skip re-exploration: the subtree was already built on the first visit.
                // The node above has been added to the tree as a leaf for this alternate path.
                if (!_globallyExploredContexts.Add(ContextKey(targetServer, remoteLoggedInUser)))
                    return;

                // Build complete map of reachable links via all transitive impersonation chains.
                // Key: (server name, local login): same server with different login mappings are separate entries.
                // Skip impersonation discovery if already sysadmin: we can already see all linked servers.
                var remoteReachableChains = isSysadmin
                    ? new List<List<string>>()
                    : GetReachableLoginChains(databaseContext);
                Logger.TraceNested($"Reachable login chains on '{targetServer}': {remoteReachableChains.Count}");

                var allLinkedServersOnThisServer = new Dictionary<(string server, string localLogin), (DataRow row, List<string> chain)>();

                // Current user's links
                try
                {
                    DataTable currentUserLinks = Links.GetLinkedServers(databaseContext);
                    foreach (DataRow row in currentUserLinks.Rows)
                    {
                        // Skip rows where we have no visibility into linked_logins
                        if (GetRowString(row, "Access") == "No visibility")
                            continue;

                        string serverLink = row["Link"].ToString();
                        string localLogin = GetRowString(row, "Local Login");
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
                    // Skip chains ending at system accounts: they add no unique linked server info and cause errors
                    if (UserService.IsSystemAccount(chain[chain.Count - 1]))
                        continue;

                    if (!TryApplyImpersonationChain(databaseContext, chain))
                        continue;
                    try
                    {
                        // Discover end-of-chain roles so the operator can see which privileges
                        // each reachable login carries (sysadmin, securityadmin, etc.).
                        // GetServerRoles populates the admin-status cache, so the IsAdmin call
                        // immediately after is free. This collapses what used to be two
                        // round-trips per chain (IsAdmin + GetServerRoles) into one.
                        List<string> chainEndRoles = databaseContext.UserService.GetServerRoles();
                        bool chainEndIsSysadmin = databaseContext.UserService.IsAdmin();

                        // Record as an escalation path only when the chain end carries
                        // sysadmin or another elevated role.
                        if (chainEndIsSysadmin || chainEndRoles.Exists(r => ElevatedRoles.Contains(r)))
                        {
                            var steps = chain.Select(login => new ImpersonationStep { Login = login, Roles = new List<string>() }).ToList();
                            steps[steps.Count - 1].Roles = chainEndRoles;
                            currentNode.EscalationPaths.Add(steps);
                        }

                        DataTable chainLinks = Links.GetLinkedServers(databaseContext);
                        foreach (DataRow row in chainLinks.Rows)
                        {
                            string serverLink = row["Link"].ToString();
                            string localLogin = GetRowString(row, "Local Login");
                            var key = (serverLink.ToUpperInvariant(), localLogin.ToUpperInvariant());
                            if (!allLinkedServersOnThisServer.ContainsKey(key))
                                allLinkedServersOnThisServer[key] = (row, chain);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Failed to query linked servers via chain [{string.Join(" -> ", chain)}]: {ex.Message}");
                    }
                    finally
                    {
                        RevertChain(databaseContext, chain.Count);
                    }
                }

                // For servers visible to the landing user (chain=null) that have an explicit remote
                // login, also create per-chain entries so the loop tries connecting while impersonated.
                // Skip empty-remote-login entries (Windows pass-through): won't work for SQL logins.
                var existingRemoteKeys = allLinkedServersOnThisServer.Keys.ToList();
                foreach (var chain in remoteReachableChains)
                {
                    if (UserService.IsSystemAccount(chain[chain.Count - 1]))
                        continue;

                    string chainEnd = chain[chain.Count - 1];
                    foreach (var key in existingRemoteKeys)
                    {
                        if (allLinkedServersOnThisServer[key].chain != null)
                            continue;

                        DataRow existingRow = allLinkedServersOnThisServer[key].row;
                        string rowRemoteLogin = GetRowString(existingRow, "Remote Login");
                        if (string.IsNullOrEmpty(rowRemoteLogin))
                            continue; // Windows pass-through: won't work for SQL logins

                        var chainKey = (key.server, chainEnd.ToUpperInvariant());
                        if (!allLinkedServersOnThisServer.ContainsKey(chainKey))
                            allLinkedServersOnThisServer[chainKey] = (existingRow, chain);
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
                    string localLogin = GetRowString(row, "Local Login");

                    if (provider.StartsWith("SQLNCLI") || provider.StartsWith("MSOLEDBSQL"))
                    {
                        // Skip entries where the local login is a system account
                        if (!UserService.IsSystemAccount(localLogin))
                            remoteSqlLinks.Add((serverLink, localLogin, row, chain));
                    }
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
                foreach (var (srv, localLog, r, ch) in remoteSqlLinks)
                {
                    string remLog = GetRowString(r, "Remote Login");
                    string accessType = GetRowString(r, "Access");
                    string via = ch != null ? $" via [{string.Join(" -> ", ch)}]" : "";

                    string desc = !string.IsNullOrEmpty(localLog)
                        ? $"{localLog} [{accessType}]" + (!string.IsNullOrEmpty(remLog) ? $" → {remLog}" : "")
                        : accessType + (!string.IsNullOrEmpty(remLog) ? $" → {remLog}" : "");

                    Logger.TraceNested($"  {srv} [{desc}]{via}");
                }

                // Explore each SQL Server link
                foreach (var (nextServer, nextLocalLogin, row, chainToReach) in remoteSqlLinks)
                {
                    // Skip if already explored with the same user context
                    string remoteLogin = nextLocalLogin;
                    // If we don't know the remote login yet, we can't pre-filter: explore and let the hash check handle it
                    if (!string.IsNullOrEmpty(remoteLogin) && _globallyExploredContexts.Contains(ContextKey(nextServer, remoteLogin)))
                    {
                        Logger.TraceNested($"Server '{nextServer}' already explored as '{remoteLogin}'. Skipping.");
                        continue;
                    }

                    // Negative cache: skip if this exact link from this source has already failed
                    // for the caller we're about to use.
                    string plannedCaller = chainToReach != null && chainToReach.Count > 0
                        ? (!string.IsNullOrEmpty(nextLocalLogin) ? nextLocalLogin : chainToReach[chainToReach.Count - 1])
                        : (!string.IsNullOrEmpty(nextLocalLogin) ? nextLocalLogin : remoteLoggedInUser);
                    if (_failedLinkAttempts.Contains(LinkAttemptKey(targetServer, nextServer, plannedCaller)))
                    {
                        Logger.TraceNested($"Skipping '{nextServer}' from '{targetServer}' as '{plannedCaller}': previous attempt was rejected.");
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

                        // If the linked server mapping requires a specific local login that's not
                        // the end of our chain, also impersonate it to use the correct mapping.
                        // If we can't, skip: the link is bound to that login and will reject us.
                        string chainEndLogin = chainToReach[chainToReach.Count - 1];
                        if (!string.IsNullOrEmpty(nextLocalLogin)
                            && !nextLocalLogin.Equals(chainEndLogin, StringComparison.OrdinalIgnoreCase)
                            && !nextLocalLogin.Equals(remoteLoggedInUser, StringComparison.OrdinalIgnoreCase))
                        {
                            if (TryApplyImpersonationChain(databaseContext, new List<string> { nextLocalLogin }))
                            {
                                impersonationHops++;
                            }
                            else
                            {
                                Logger.TraceNested($"Cannot impersonate required local login '{nextLocalLogin}' on '{targetServer}' for link to '{nextServer}'. Skipping.");
                                RevertChain(databaseContext, impersonationHops);
                                continue;
                            }
                        }
                    }
                    else if (!string.IsNullOrEmpty(nextLocalLogin) && !nextLocalLogin.Equals(remoteLoggedInUser, StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryApplyImpersonationChain(databaseContext, new List<string> { nextLocalLogin }))
                        {
                            impersonationHops = 1;
                        }
                        else
                        {
                            // The link is bound to nextLocalLogin and we can't become it: skip.
                            // Trying anyway as the current login would fail with 'no login-mapping exists'.
                            Logger.TraceNested($"Cannot impersonate required local login '{nextLocalLogin}' on '{targetServer}' for link to '{nextServer}'. Skipping.");
                            continue;
                        }
                    }

                    try
                    {
                        // Determine the impersonation chain used on this server to reach the next link
                        List<string> nextImpersonationLogins;
                        if (chainToReach != null && chainToReach.Count > 0)
                        {
                            nextImpersonationLogins = new List<string>(chainToReach);
                            // If we additionally impersonated the local login, include it
                            string chainEndLogin = chainToReach[chainToReach.Count - 1];
                            if (!string.IsNullOrEmpty(nextLocalLogin)
                                && !nextLocalLogin.Equals(chainEndLogin, StringComparison.OrdinalIgnoreCase)
                                && !nextLocalLogin.Equals(remoteLoggedInUser, StringComparison.OrdinalIgnoreCase)
                                && impersonationHops > chainToReach.Count)
                            {
                                nextImpersonationLogins.Add(nextLocalLogin);
                            }
                        }
                        else if (!string.IsNullOrEmpty(nextLocalLogin) && !nextLocalLogin.Equals(remoteLoggedInUser, StringComparison.OrdinalIgnoreCase))
                        {
                            nextImpersonationLogins = new List<string> { nextLocalLogin };
                        }
                        else
                        {
                            nextImpersonationLogins = new List<string>();
                        }

                        List<ImpersonationStep> nextImpersonation = BuildImpersonationSteps(nextImpersonationLogins);

                        // Recurse: ExploreLinkedServer will AddToChain/RemoveLastFromChain internally
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
                string asWho = FormatImpersonationContext(parentImpersonationChain, parentNode.LoggedInUser);
                Logger.TraceNested($"Failed to explore {targetServer} as [{asWho}]: {ex.Message}");
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

                    // Only keep the shortest chain to each unique end login.
                    // For linked server discovery, we only care about what login we end up as,
                    // not the intermediate path: same end login sees the same linked servers.
                    var shortestByEndLogin = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

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
                        {
                            if (!shortestByEndLogin.TryGetValue(endLogin, out var existing) || chain.Count < existing.Count)
                                shortestByEndLogin[endLogin] = chain;
                        }
                    }

                    chains.AddRange(shortestByEndLogin.Values);
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
        /// Builds ImpersonationStep list for each login in the chain.
        /// Steps carry only the login name; roles are intentionally omitted here because
        /// BuildImpersonationSteps is called before knowing whether the next-hop connection
        /// will succeed, so querying roles speculatively wastes N remote round-trips per
        /// failed attempt. Destination node roles are captured separately via UserService.GetServerRoles
        /// inside ExploreLinkedServer after the connection probe succeeds.
        /// </summary>
        private static List<ImpersonationStep> BuildImpersonationSteps(List<string> logins)
        {
            if (logins == null || logins.Count == 0)
                return new List<ImpersonationStep>();

            return logins.Select(l => new ImpersonationStep { Login = l }).ToList();
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
        /// Counts all privilege escalation paths across the entire tree.
        /// </summary>
        private int CountEscalationPaths(ServerNode node)
        {
            int count = node.EscalationPaths.Count;
            foreach (var child in node.Children)
            {
                count += CountEscalationPaths(child);
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

            // Format display name
            string displayName = node.Alias;
            if (!node.Alias.Equals(node.ActualName, StringComparison.OrdinalIgnoreCase))
            {
                displayName = $"{node.Alias} [{node.ActualName}]";
            }

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
                    bool childIsLast = (i == node.Children.Count - 1) && node.EscalationPaths.Count == 0;
                    DisplayTreeNode(node.Children[i], serverChildIndent, childIsLast, currentPath);
                }

                RenderEscalationPaths(node, serverChildIndent);
            }
            else
            {
                // No impersonation: render directly
                string connector = isLast ? "╚══ " : "╠══ ";
                string childIndent = indent + (isLast ? "    " : "║   ");

                Console.WriteLine($"{indent}{connector}{displayName} ({node.LoggedInUser} [{node.MappedUser}]){node.PrivilegeMarker}");

                if (node.NonSqlLinks.Count > 0)
                {
                    Console.WriteLine($"{childIndent}└── [OPENQUERY] {string.Join(", ", node.NonSqlLinks)}");
                }

                for (int i = 0; i < node.Children.Count; i++)
                {
                    bool childIsLast = (i == node.Children.Count - 1) && node.EscalationPaths.Count == 0;
                    DisplayTreeNode(node.Children[i], childIndent, childIsLast, currentPath);
                }

                RenderEscalationPaths(node, childIndent);
            }
        }

        /// <summary>
        /// Renders privilege escalation paths discovered at a server node.
        /// Shows impersonation chains that lead to sysadmin or elevated roles.
        /// </summary>
        private static void RenderEscalationPaths(ServerNode node, string indent)
        {
            if (node.EscalationPaths.Count == 0) return;

            for (int p = 0; p < node.EscalationPaths.Count; p++)
            {
                var path = node.EscalationPaths[p];
                bool isLast = (p == node.EscalationPaths.Count - 1);
                string connector = isLast ? "└── " : "├── ";

                string chainDisplay = string.Join(" → ", path.Select(s => s.Login + s.PrivilegeMarker));
                Console.WriteLine($"{indent}{connector}{chainDisplay}");
            }
        }

        /// <summary>
        /// Builds a summary DataTable of all discovered chains and displays them grouped by privilege.
        /// Sorted by privilege level (sysadmin first), then by hop count (shortest path first).
        /// </summary>
        private DataTable DisplayChainCommands()
        {
            DataTable result = new DataTable();
            result.Columns.Add("Endpoint", typeof(string));
            result.Columns.Add("Login", typeof(string));
            result.Columns.Add("Mapped To", typeof(string));
            result.Columns.Add("Hops", typeof(int));
            result.Columns.Add("Server Roles", typeof(string));
            result.Columns.Add("Command", typeof(string));

            // Sort: privileged first, then shortest hop count
            var ordered = _allChains
                .Where(c => c.Count > 0)
                .OrderByDescending(c => GetChainPriority(c))
                .ThenBy(c => GetTotalHops(c));

            foreach (var chain in ordered)
            {
                int hops = GetTotalHops(chain);
                var row = BuildChainRow(chain, hops);
                result.Rows.Add(row);

                // Add escalation path rows for the last node in this chain
                var lastNode = chain[chain.Count - 1];
                foreach (var escalation in lastNode.EscalationPaths)
                {
                    int escHops = hops + escalation.Count;
                    var escRow = BuildEscalationRow(chain, escalation, escHops);
                    result.Rows.Add(escRow);
                }
            }

            if (result.Rows.Count > 0)
            {
                Console.WriteLine(OutputFormatter.ConvertDataTable(result));
            }

            return result;
        }

        /// <summary>
        /// Returns the total number of hops (linked servers + impersonation steps) in a chain.
        /// </summary>
        private static int GetTotalHops(List<ServerNode> chain)
        {
            int hops = chain.Count; // linked server hops
            foreach (var node in chain)
                hops += node.ImpersonationChain.Count; // impersonation hops on parent
            return hops;
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
        private object[] BuildChainRow(List<ServerNode> chain, int hops)
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

            // Build host argument: starting impersonation + any chain[0] impersonation on the root server
            string hostArg = Misc.BracketIdentifier(_rootNode.Alias);
            var hostImpersonation = new List<string>(_startingImpersonation);
            if (chain.Count > 0 && chain[0].ImpersonationChain.Count > 0)
                hostImpersonation.AddRange(chain[0].ImpersonationChain.ConvertAll(s => s.Login));
            if (hostImpersonation.Count > 0)
                hostArg += "/" + string.Join("/", hostImpersonation);

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

            return new object[] { endpoint, login, mappedTo, hops, privilege, command };
        }

        /// <summary>
        /// Builds a DataRow array for a privilege escalation path at the end of a chain.
        /// The command includes the linked server path with escalation impersonation on the final server.
        /// </summary>
        private object[] BuildEscalationRow(List<ServerNode> chain, List<ImpersonationStep> escalation, int hops)
        {
            var lastNode = chain[chain.Count - 1];

            // Build linked server list: same as BuildChainRow but with escalation on the last server
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

            // Add escalation impersonation on the last server in the chain
            serverList[serverList.Count - 1].ImpersonationUsers = escalation.ConvertAll(s => s.Login).ToArray();

            LinkedServers linkedServers = new LinkedServers(serverList.ToArray());
            string chainArg = linkedServers.GetChainArguments();

            // Build host argument: starting impersonation + any chain[0] impersonation on the root server
            string hostArg = Misc.BracketIdentifier(_rootNode.Alias);
            var hostImpersonation = new List<string>(_startingImpersonation);
            if (chain.Count > 0 && chain[0].ImpersonationChain.Count > 0)
                hostImpersonation.AddRange(chain[0].ImpersonationChain.ConvertAll(s => s.Login));
            if (hostImpersonation.Count > 0)
                hostArg += "/" + string.Join("/", hostImpersonation);

            // Full command
            string command = $"\"{hostArg}\" -l \"{chainArg}\"";

            // Endpoint is same server but with escalated login
            string endpoint = lastNode.Alias;
            if (!lastNode.Alias.Equals(lastNode.ActualName, StringComparison.OrdinalIgnoreCase))
            {
                endpoint = $"{lastNode.Alias} [{lastNode.ActualName}]";
            }

            // Login is the end of the escalation chain
            var lastStep = escalation[escalation.Count - 1];
            string login = lastStep.Login;
            string mappedTo = "";

            // Privilege level from the escalation endpoint
            string privilege;
            if (lastStep.IsSysadmin)
                privilege = "sysadmin";
            else if (lastStep.Roles.Exists(r => ElevatedRoles.Contains(r)))
                privilege = string.Join(", ", lastStep.Roles.FindAll(r => ElevatedRoles.Contains(r)));
            else
                privilege = string.Join(", ", lastStep.Roles);

            return new object[] { endpoint, login, mappedTo, hops, privilege, command };
        }

    }
}
