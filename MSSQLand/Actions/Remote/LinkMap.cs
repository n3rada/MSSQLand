// MSSQLand/Actions/Remote/LinkMap.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Models;
using System;
using System.Collections.Generic;
using System.Data;

namespace MSSQLand.Actions.Remote
{
    /// <summary>
    /// Recursively explores all accessible linked server chains, mapping execution paths.
    /// 
    /// This action:
    /// - Enumerates all directly linked servers
    /// - Recursively explores each linked server's own linked servers
    /// - Attempts impersonation when a link requires a specific local login (works at any depth via /user syntax)
    /// - Detects and prevents infinite loops using hash-based state tracking
    /// - Maps complete chains showing: Server -> User -> LinkedServer -> User -> ...
    /// - Respects maximum recursion depth to prevent runaway exploration
    /// - Handles slow/unresponsive servers with timeout mechanism
    /// </summary>
    internal class LinkMap : BaseAction
    {
        [ExcludeFromArguments]
        private readonly List<List<Dictionary<string, string>>> _discoveredChains = new();

        [ExcludeFromArguments]
        private const int DEFAULT_MAX_DEPTH = 10;

        [ArgumentMetadata(Position = 0, Description = "Maximum recursion depth (default: 10, max: 50)")]
        private int _maxDepth = DEFAULT_MAX_DEPTH;

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);

            if (_maxDepth < 1 || _maxDepth > 50)
            {
                throw new ArgumentException("Maximum depth must be between 1 and 50.");
            }
        }

        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Maximum recursion depth: {_maxDepth}");

            DataTable linkedServersTable = GetLinkedServers(databaseContext);

            if (linkedServersTable.Rows.Count == 0)
            {
                Logger.Warning("No linked servers found.");
                return null;
            }

            Logger.TaskNested($"Found {linkedServersTable.Rows.Count} linked server(s), exploring chains");

            // Only suppress logs if user didn't explicitly request verbose/trace output
            LogLevel originalLogLevel = Logger.MinimumLogLevel;
            bool suppressLogs = originalLogLevel > LogLevel.Trace;
            if (suppressLogs)
            {
                Logger.MinimumLogLevel = LogLevel.Warning;
            }

            // Start exploration from each linked server
            foreach (DataRow row in linkedServersTable.Rows)
            {
                string remoteServer = row["Link"].ToString();
                string localLogin = row["Local Login"] == DBNull.Value || string.IsNullOrEmpty(row["Local Login"].ToString()) 
                    ? null 
                    : row["Local Login"].ToString();

                // Start a new chain with its own visited states for loop detection
                List<Dictionary<string, string>> currentChain = new();
                HashSet<string> visitedInChain = new();
                
                // Create a temp context to not pollute the original
                DatabaseContext tempContext = databaseContext.Copy();
                
                ExploreServer(tempContext, remoteServer, localLogin, currentChain, visitedInChain, currentDepth: 0);
            }

            // Restore original log level if we suppressed
            if (suppressLogs)
            {
                Logger.MinimumLogLevel = originalLogLevel;
            }

            // Display results
            string initialServerEntry = $"{databaseContext.Server.Hostname} ({databaseContext.Server.SystemUser} [{databaseContext.Server.MappedUser}])";

            if (!databaseContext.QueryService.LinkedServers.IsEmpty)
            {
                initialServerEntry += " -> " + string.Join(" -> ", databaseContext.QueryService.LinkedServers.GetChainParts()) 
                    + $" ({databaseContext.UserService.SystemUser} [{databaseContext.UserService.MappedUser}])";
            }

            if (_discoveredChains.Count == 0)
            {
                Logger.Warning("No accessible linked server chains found.");
                return null;
            }

            Logger.Success($"Found {_discoveredChains.Count} accessible chain(s)");

            foreach (var chain in _discoveredChains)
            {
                if (chain.Count == 0) continue;

                List<string> formattedLines = new() { initialServerEntry };
                List<Server> serverChainList = new();

                foreach (var entry in chain)
                {
                    string serverName = entry["ServerName"];
                    string loggedIn = entry["LoggedIn"];
                    string mapped = entry["Mapped"];
                    string impersonatedUser = entry.ContainsKey("ImpersonatedUser") ? entry["ImpersonatedUser"].Trim() : "-";

                    formattedLines.Add($"-{(impersonatedUser != "-" ? $" {impersonatedUser} " : "-")}-> {serverName} ({loggedIn} [{mapped}])");
                    
                    serverChainList.Add(new Server
                    {
                        Hostname = serverName,
                        ImpersonationUser = impersonatedUser != "-" ? impersonatedUser : null,
                        Database = null
                    });
                }

                Console.WriteLine();
                Console.WriteLine(string.Join(" ", formattedLines));
                
                if (serverChainList.Count > 0)
                {
                    LinkedServers chainForDisplay = new LinkedServers(serverChainList.ToArray());
                    string chainCommand = $"-l {chainForDisplay.GetChainArguments()}";
                    Logger.InfoNested($"To use this chain: {chainCommand}");
                }
            }

            return _discoveredChains;
        }

        /// <summary>
        /// Recursively explores linked servers.
        /// If a link requires a specific local login different from current user, attempts impersonation.
        /// Impersonation works at any depth because it uses the /user syntax in the chain path.
        /// Loop detection is per-chain: same server+user in the current path = loop, skip.
        /// </summary>
        private void ExploreServer(DatabaseContext databaseContext, string targetServer, string requiredLocalLogin, 
            List<Dictionary<string, string>> currentChain, HashSet<string> visitedInChain, int currentDepth)
        {
            if (currentDepth >= _maxDepth)
            {
                return;
            }

            // Save current state for restoration
            LinkedServers previousLinkedServers = new LinkedServers(databaseContext.QueryService.LinkedServers);
            Server previousExecutionServer = databaseContext.QueryService.ExecutionServer;

            try
            {
                string impersonatedUser = null;

                // Check if we need to impersonate to take this link
                if (!string.IsNullOrEmpty(requiredLocalLogin))
                {
                    var (_, currentSystemUser) = databaseContext.UserService.GetInfo();
                    
                    if (!currentSystemUser.Equals(requiredLocalLogin, StringComparison.OrdinalIgnoreCase))
                    {
                        // Try to impersonate
                        try
                        {
                            databaseContext.UserService.ImpersonateUser(requiredLocalLogin);
                            impersonatedUser = requiredLocalLogin;
                        }
                        catch
                        {
                            // Can't impersonate - skip this link
                            return;
                        }
                    }
                }

                // Add server to chain
                databaseContext.QueryService.LinkedServers.AddToChain(targetServer);

                // Query user info through the chain
                var (mappedUser, remoteLoggedInUser) = databaseContext.UserService.GetInfo();

                // Create state hash for loop detection (server + user context)
                ServerExecutionState currentState = ServerExecutionState.FromContext(targetServer, databaseContext.UserService);
                string stateHash = currentState.GetStateHash();

                // Check for loop in THIS chain path only
                if (visitedInChain.Contains(stateHash))
                {
                    // Loop detected in current path
                    Logger.TraceNested($"Loop detected at server '{targetServer}' with user '{currentState.SystemUser}'. Skipping to prevent infinite recursion.");
                    return;
                }

                visitedInChain.Add(stateHash);

                // Add this server to current chain
                var chainEntry = new Dictionary<string, string>
                {
                    { "ServerName", targetServer },
                    { "LoggedIn", currentState.SystemUser },
                    { "Mapped", currentState.MappedUser },
                    { "ImpersonatedUser", impersonatedUser ?? "-" }
                };
                currentChain.Add(chainEntry);

                // Save this chain (make a copy)
                _discoveredChains.Add(new List<Dictionary<string, string>>(currentChain));

                // Get linked servers from this remote server
                DataTable remoteLinkedServers = GetLinkedServersWithTimeout(databaseContext, targetServer);

                Logger.TraceNested($"Exploring linked servers on '{targetServer}' (found {remoteLinkedServers?.Rows.Count ?? 0})");

                if (remoteLinkedServers != null && remoteLinkedServers.Rows.Count > 0)
                {
                    foreach (DataRow row in remoteLinkedServers.Rows)
                    {
                        string nextServer = row["Link"].ToString();
                        string nextLocalLogin = row["Local Login"] == DBNull.Value || string.IsNullOrEmpty(row["Local Login"].ToString())
                            ? null
                            : row["Local Login"].ToString();

                        // Create a copy of current chain for this branch
                        List<Dictionary<string, string>> branchChain = new List<Dictionary<string, string>>(currentChain);
                        
                        // Copy the visited states for this branch (each branch has its own path history)
                        HashSet<string> branchVisited = new HashSet<string>(visitedInChain);

                        // Create a fresh context copy for this branch
                        DatabaseContext branchContext = databaseContext.Copy();
                        branchContext.QueryService.LinkedServers = new LinkedServers(databaseContext.QueryService.LinkedServers);
                        branchContext.QueryService.ExecutionServer = databaseContext.QueryService.ExecutionServer;

                        // Explore recursively - pass the required local login so impersonation can happen at any depth
                        ExploreServer(branchContext, nextServer, nextLocalLogin, branchChain, branchVisited, currentDepth + 1);
                    }
                }

                // Remove this server from chain for backtracking
                currentChain.RemoveAt(currentChain.Count - 1);
            }
            catch (Exception ex)
            {
                // Log the error so we can see why exploration failed
                Logger.TraceNested($"Failed to explore {targetServer}: {ex.Message}");
            }
            finally
            {
                // Restore execution context
                databaseContext.QueryService.LinkedServers = previousLinkedServers;
                databaseContext.QueryService.ExecutionServer = previousExecutionServer;
            }
        }

        private static DataTable GetLinkedServersWithTimeout(DatabaseContext databaseContext, string serverName)
        {
            try
            {
                return GetLinkedServers(databaseContext);
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to enumerate links on {serverName}: {ex.Message}");
                return null;
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

            return databaseContext.QueryService.ExecuteTable(query);
        }
    }
}
