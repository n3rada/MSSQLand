// MSSQLand/Actions/Remote/LinkMap.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using MSSQLand.Models;
using System;
using System.Collections.Generic;
using System.Data;
using static MSSQLand.Utilities.Logger;

namespace MSSQLand.Actions.Remote
{
    /// <summary>
    /// Recursively explores all accessible linked server chains, mapping execution paths.
    /// 
    /// This action:
    /// - Enumerates all directly linked servers
    /// - Recursively explores each linked server's own linked servers
    /// - Handles user impersonation with proper stack management
    /// - Detects and prevents infinite loops using hash-based state tracking
    /// - Maps complete chains showing: Server -> User -> LinkedServer -> User -> ...
    /// - Respects maximum recursion depth to prevent runaway exploration
    /// - Handles slow/unresponsive servers with timeout mechanism
    /// - Properly restores execution context after recursion
    /// 
    /// Key Features:
    /// - Loop detection: Uses ServerExecutionState hashing (hostname + users + sysadmin) to prevent infinite recursion
    /// - Impersonation stack: Tracks and properly reverts all impersonations in LIFO order
    /// - Depth limiting: Configurable maximum depth (default: 10 levels)
    /// - Timeout handling: Leverages QueryService's built-in timeout with exponential backoff
    /// - State restoration: Restores LinkedServers chain and ExecutionServer after each recursion
    /// - Graceful degradation: Continues mapping accessible paths when servers are unreachable
    /// </summary>
    internal class LinkMap : BaseAction
    {
        [ExcludeFromArguments]
        private readonly Dictionary<Guid, List<Dictionary<string, string>>> _serverMapping = new();

        [ExcludeFromArguments]
        private readonly Dictionary<Guid, HashSet<string>> _visitedStates = new();

        [ExcludeFromArguments]
        private readonly Dictionary<Guid, Stack<string>> _impersonationStack = new();

        [ExcludeFromArguments]
        private const int DEFAULT_MAX_DEPTH = 10;

        [ArgumentMetadata(Position = 0, Description = "Maximum recursion depth (default: 10, max: 50)")]
        private int _maxDepth = DEFAULT_MAX_DEPTH;

        public override void ValidateArguments(string[] args)
        {
            string[] parts = args;

            if (parts != null && parts.Length > 0)
            {
                if (!int.TryParse(parts[0], out int maxDepth) || maxDepth < 1 || maxDepth > 50)
                {
                    throw new ArgumentException("Maximum depth must be between 1 and 50. Example: /a:links explore 15");
                }
                _maxDepth = maxDepth;
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Enumerating linked servers");
            Logger.TaskNested($"Maximum recursion depth: {_maxDepth}");

            DataTable linkedServersTable = GetLinkedServers(databaseContext);

            if (linkedServersTable.Rows.Count == 0)
            {
                Logger.Warning("No linked servers found.");
                return null;
            }

            Logger.TaskNested("Exploring all possible linked server chains");

            // Suppress Info/Task/Success logs during exploration to reduce noise
            LogLevel originalLogLevel = Logger.MinimumLogLevel;
            Logger.MinimumLogLevel = LogLevel.Warning;

            foreach (DataRow row in linkedServersTable.Rows)
            {
                string remoteServer = row["Link"].ToString();
                string localLogin = row["Local Login"] == DBNull.Value || string.IsNullOrEmpty(row["Local Login"].ToString()) 
                    ? "<Current Context>" 
                    : row["Local Login"].ToString();

                Guid chainId = Guid.NewGuid();
                _serverMapping[chainId] = new List<Dictionary<string, string>>();
                _visitedStates[chainId] = new HashSet<string>();
                _impersonationStack[chainId] = new Stack<string>();

                DatabaseContext tempDatabaseContext = databaseContext.Copy();

                // Start exploration with depth 0
                ExploreServer(tempDatabaseContext, remoteServer, localLogin, chainId, currentDepth: 0);

                // Revert all impersonations in LIFO order
                RevertAllImpersonations(tempDatabaseContext.UserService, chainId);
            }

            // Restore original log level
            Logger.MinimumLogLevel = originalLogLevel;

            string initialServerEntry = $"{databaseContext.Server.Hostname} ({databaseContext.Server.SystemUser} [{databaseContext.Server.MappedUser}])";

            Logger.Debug($"Initial server entry: {initialServerEntry}");

            if (!databaseContext.QueryService.LinkedServers.IsEmpty)
            {
                initialServerEntry += " -> " + string.Join(" -> ", databaseContext.QueryService.LinkedServers.GetChainParts()) + $" ({databaseContext.UserService.SystemUser} [{databaseContext.UserService.MappedUser}])";
                Logger.DebugNested($"Chain added: {initialServerEntry}");
            }

            Logger.Success("Accessible linked servers chain");

            foreach (var chainEntry in _serverMapping)
            {
                List<Dictionary<string, string>> chainMapping = chainEntry.Value;
                List<string> formattedLines = new() { initialServerEntry };
                List<string> chainParts = new();

                foreach (var entry in chainMapping)
                {
                    string serverName = entry["ServerName"];
                    string loggedIn = entry["LoggedIn"];
                    string mapped = entry["Mapped"];
                    string impersonatedUser = entry["ImpersonatedUser"].Trim();

                    formattedLines.Add($"-{impersonatedUser}-> {serverName} ({loggedIn} [{mapped}])");
                    
                    // Build chain command
                    string chainPart;
                    if (impersonatedUser != "-")
                    {
                        chainPart = $"{serverName}/{impersonatedUser}";
                    }
                    else
                    {
                        chainPart = serverName;
                    }
                    
                    // Add brackets if the part contains a semicolon
                    if (chainPart.Contains(";"))
                    {
                        chainPart = $"[{chainPart}]";
                    }
                    
                    chainParts.Add(chainPart);
                }

                Console.WriteLine();
                Console.WriteLine(string.Join(" ", formattedLines));
                
                // Show command to reproduce this chain
                if (chainParts.Count > 0)
                {
                    string chainCommand = $"-l {string.Join(";", chainParts)}";
                    Logger.InfoNested($"To use this chain: {chainCommand}");
                }
            }

            return _serverMapping;
        }

        /// <summary>
        /// Recursively explores linked servers with proper state management.
        /// </summary>
        /// <param name="databaseContext">Current database context.</param>
        /// <param name="targetServer">Target linked server to explore.</param>
        /// <param name="expectedLocalLogin">Expected login for accessing the linked server.</param>
        /// <param name="chainId">Unique identifier for the current exploration chain.</param>
        /// <param name="currentDepth">Current recursion depth (0-based).</param>
        private void ExploreServer(DatabaseContext databaseContext, string targetServer, string expectedLocalLogin, Guid chainId, int currentDepth)
        {
            // Check maximum depth limit
            if (currentDepth >= _maxDepth)
            {
                Logger.Warning($"Maximum recursion depth ({_maxDepth}) reached at {targetServer}");
                Logger.WarningNested("Stopping exploration to prevent excessive recursion. Use argument to increase depth.");
                return;
            }

            Logger.Debug($"Accessing linked server: {targetServer} (depth: {currentDepth})");

            // Save current state for restoration
            LinkedServers previousLinkedServers = new LinkedServers(databaseContext.QueryService.LinkedServers);
            string previousExecutionServer = databaseContext.QueryService.ExecutionServer;

            try
            {
                // Check if we are already logged in with the correct user
                var (currentUser, systemUser) = databaseContext.UserService.GetInfo();
                Logger.DebugNested($"[{databaseContext.QueryService.ExecutionServer}] LoggedIn: {systemUser}, Mapped: {currentUser}");

                string impersonatedUser = null;

                // Only attempt impersonation if expected login is not current context and doesn't match current user
                if (expectedLocalLogin != "<Current Context>" && systemUser != expectedLocalLogin)
                {
                    Logger.DebugNested($"Current user '{systemUser}' does not match expected local login '{expectedLocalLogin}'");
                    Logger.DebugNested("Attempting impersonation");

                    if (databaseContext.UserService.CanImpersonate(expectedLocalLogin))
                    {
                        databaseContext.UserService.ImpersonateUser(expectedLocalLogin);
                        impersonatedUser = expectedLocalLogin;
                        
                        // Track impersonation in stack for proper LIFO reversion
                        _impersonationStack[chainId].Push(expectedLocalLogin);
                        
                        Logger.DebugNested($"[{databaseContext.QueryService.ExecutionServer}] Impersonated '{expectedLocalLogin}' to access {targetServer}.");
                    }
                    else
                    {
                        Logger.Warning($"[{databaseContext.QueryService.ExecutionServer}] Cannot impersonate {expectedLocalLogin} on {targetServer}. Skipping.");
                        return;
                    }
                }
                else if (expectedLocalLogin == "<Current Context>")
                {
                    Logger.DebugNested("Linked server uses current security context (no explicit login mapping)");
                }

                // Update the linked server chain
                databaseContext.QueryService.LinkedServers.AddToChain(targetServer);
                databaseContext.QueryService.ExecutionServer = targetServer;

                // Query user info THROUGH the linked server chain
                var (mappedUser, remoteLoggedInUser) = databaseContext.UserService.GetInfo();

                // Create ServerExecutionState for loop detection - this now queries through the chain
                ServerExecutionState currentState = ServerExecutionState.FromContext(
                    targetServer, 
                    databaseContext.UserService
                );

                string stateHash = currentState.GetStateHash();

                // Check for loops
                if (_visitedStates[chainId].Contains(stateHash))
                {
                    Logger.Warning($"Detected loop at {targetServer} with same execution state: {currentState}");
                    Logger.WarningNested("Skipping to prevent infinite recursion.");
                    return;
                }

                // Mark this state as visited
                _visitedStates[chainId].Add(stateHash);

                Logger.Debug($"Adding mapping for {targetServer}");
                Logger.DebugNested($"LoggedIn User: {currentState.SystemUser}");
                Logger.DebugNested($"Mapped User: {currentState.MappedUser}");
                Logger.DebugNested($"Is Sysadmin: {currentState.IsSysadmin}");
                Logger.DebugNested($"Impersonated User: {impersonatedUser}");
                Logger.DebugNested($"State Hash: {stateHash}");

                _serverMapping[chainId].Add(new Dictionary<string, string>
                {
                    { "ServerName", targetServer },
                    { "LoggedIn", currentState.SystemUser },
                    { "Mapped", currentState.MappedUser },
                    { "ImpersonatedUser", !string.IsNullOrEmpty(impersonatedUser) ? $" {impersonatedUser} " : "-" }
                });

                Logger.DebugNested($"[{databaseContext.QueryService.ExecutionServer}] LoggedIn: {remoteLoggedInUser}, Mapped: {mappedUser}");

                // Retrieve linked servers from remote server
                DataTable remoteLinkedServers = GetLinkedServersWithTimeout(databaseContext, targetServer);

                if (remoteLinkedServers == null || remoteLinkedServers.Rows.Count == 0)
                {
                    Logger.Debug($"No further linked servers found on {targetServer}");
                    return;
                }

                // Explore each linked server recursively
                foreach (DataRow row in remoteLinkedServers.Rows)
                {
                    string nextServer = row["Link"].ToString();
                    string nextLocalLogin = row["Local Login"].ToString();

                    // Create a new context copy for each branch to avoid state pollution
                    DatabaseContext branchContext = databaseContext.Copy();
                    
                    // Copy current linked servers state
                    branchContext.QueryService.LinkedServers = new LinkedServers(databaseContext.QueryService.LinkedServers);
                    branchContext.QueryService.ExecutionServer = databaseContext.QueryService.ExecutionServer;

                    ExploreServer(branchContext, nextServer, nextLocalLogin, chainId, currentDepth + 1);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error exploring {targetServer}: {ex.Message}");
                Logger.ErrorNested("Continuing with next server...");
            }
            finally
            {
                // Restore execution context
                databaseContext.QueryService.LinkedServers = previousLinkedServers;
                databaseContext.QueryService.ExecutionServer = previousExecutionServer;
            }
        }

        /// <summary>
        /// Retrieves linked servers with timeout handling.
        /// </summary>
        /// <param name="databaseContext">Current database context.</param>
        /// <param name="serverName">Name of the server being queried.</param>
        /// <returns>DataTable with linked servers or null on timeout/error.</returns>
        private static DataTable GetLinkedServersWithTimeout(DatabaseContext databaseContext, string serverName)
        {
            try
            {
                return GetLinkedServers(databaseContext);
            }
            catch (System.Data.SqlClient.SqlException ex) when (ex.Message.Contains("Timeout"))
            {
                Logger.Warning($"Timeout querying linked servers on {serverName}");
                Logger.WarningNested("Server may be slow or unresponsive. Skipping further exploration.");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Error querying linked servers on {serverName}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reverts all impersonations in LIFO (Last In, First Out) order.
        /// </summary>
        /// <param name="userService">UserService instance.</param>
        /// <param name="chainId">Chain identifier.</param>
        private void RevertAllImpersonations(UserService userService, Guid chainId)
        {
            if (!_impersonationStack.ContainsKey(chainId) || _impersonationStack[chainId].Count == 0)
            {
                return;
            }

            int count = _impersonationStack[chainId].Count;
            Logger.Debug($"Reverting {count} impersonation(s) in LIFO order");

            while (_impersonationStack[chainId].Count > 0)
            {
                string impersonatedUser = _impersonationStack[chainId].Pop();
                try
                {
                    userService.RevertImpersonation();
                    Logger.DebugNested($"Reverted impersonation of '{impersonatedUser}'");
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to revert impersonation of '{impersonatedUser}': {ex.Message}");
                }
            }
        }

        private static DataTable GetLinkedServers(DatabaseContext databaseContext)
        {
            string query = @"
        SELECT 
            srv.name AS [Link], 
            prin.name AS [Local Login],
            ll.remote_name AS [Remote Login]
        FROM master.sys.servers srv
        LEFT JOIN master.sys.linked_logins ll 
            ON srv.server_id = ll.server_id
        LEFT JOIN master.sys.server_principals prin 
            ON ll.local_principal_id = prin.principal_id
        WHERE srv.is_linked = 1
        ORDER BY srv.name;";

            DataTable results = databaseContext.QueryService.ExecuteTable(query);
            Logger.Debug(OutputFormatter.ConvertDataTable(results));
            return results;
        }

    }
}
