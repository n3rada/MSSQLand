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
    /// </summary>
    internal class LinkMap : BaseAction
    {
        [ExcludeFromArguments]
        private readonly List<List<Dictionary<string, string>>> _discoveredChains = new();

        [ArgumentMetadata(Position = 0, Description = "Maximum recursion depth (default: 10, max: 50)")]
        private int _maxDepth = 10;

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

            DataTable linkedServersTable = QueryLinkedServers(databaseContext);

            if (linkedServersTable.Rows.Count == 0)
            {
                Logger.Warning("No linked servers found.");
                return null;
            }

            Logger.TaskNested($"Linked servers on initial server: {linkedServersTable.Rows.Count}");

            // Compute starting server's state hash for loop detection
            // This prevents chains that loop back to the starting point
            ServerExecutionState startingState = ServerExecutionState.FromContext(
                databaseContext.Server.Hostname, 
                databaseContext.UserService
            );
            string startingHash = startingState.GetStateHash();

            // Start exploration from each linked server
            foreach (DataRow row in linkedServersTable.Rows)
            {
                string remoteServer = row["Link"].ToString();
                string localLogin = row["Local Login"] == DBNull.Value || string.IsNullOrEmpty(row["Local Login"].ToString()) 
                    ? null 
                    : row["Local Login"].ToString();

                // Start a new chain with its own visited states for loop detection
                // Include the starting server to detect loops back to origin
                List<Dictionary<string, string>> currentChain = new();
                HashSet<string> visitedInChain = new() { startingHash };
                
                // Create a temp context to not pollute the original
                DatabaseContext tempContext = databaseContext.Copy();
                
                ExploreLinkedServer(tempContext, remoteServer, localLogin, currentChain, visitedInChain, currentDepth: 0);
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
        private void ExploreLinkedServer(DatabaseContext databaseContext, string targetServer, string requiredLocalLogin, 
            List<Dictionary<string, string>> currentChain, HashSet<string> visitedInChain, int currentDepth)
        {
            if (currentDepth >= _maxDepth)
            {
                Logger.TraceNested($"Maximum depth {_maxDepth} reached at server '{targetServer}'. Backtracking."); 
                return;
            }


            try
            {
                string impersonatedUser = null;

                // Check if we need to impersonate to take this link
                if (!string.IsNullOrEmpty(requiredLocalLogin))
                {
                    Logger.TraceNested($"Link to '{targetServer}' requires local login '{requiredLocalLogin}'");    
                    var (_, currentSystemUser) = databaseContext.UserService.GetInfo();
                    
                    if (!currentSystemUser.Equals(requiredLocalLogin, StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.TraceNested($"Impersonating user '{requiredLocalLogin}' on server '{targetServer}'");
                        // Try to impersonate
                        try
                        {
                            databaseContext.UserService.ImpersonateUser(requiredLocalLogin);
                            impersonatedUser = requiredLocalLogin;
                        }
                        catch
                        {
                            Logger.TraceNested($"Failed to impersonate user '{requiredLocalLogin}' on server '{targetServer}'. Skipping this link.");
                            return;
                        }
                    }
                }

                // Add server to chain
                databaseContext.QueryService.LinkedServers.AddToChain(targetServer);

                // Query user info through the chain
                var (mappedUser, remoteLoggedInUser) = databaseContext.UserService.GetInfo();
                Logger.TraceNested($"Logged in to server '{targetServer}' as: '{remoteLoggedInUser}' [{mappedUser}]");

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
                DataTable remoteLinkedServers;
                try
                {
                    remoteLinkedServers = QueryLinkedServers(databaseContext);
                }
                catch (Exception ex)
                {
                    Logger.TraceNested($"Failed to enumerate links on {targetServer}: {ex.Message}");
                    return;
                }

                Logger.TraceNested($"Exploring linked servers on '{targetServer}' (found {remoteLinkedServers.Rows.Count})");

                foreach (DataRow row in remoteLinkedServers.Rows)
                {
                    string nextServer = row["Link"].ToString();
                    string nextLocalLogin = row["Local Login"] == DBNull.Value || string.IsNullOrEmpty(row["Local Login"].ToString())
                        ? null
                        : row["Local Login"].ToString();

                    // Create copies for this branch (each branch has its own isolated state)
                    List<Dictionary<string, string>> branchChain = new(currentChain);
                    HashSet<string> branchVisited = new(visitedInChain);
                    DatabaseContext branchContext = databaseContext.Copy();

                    // Explore recursively
                    ExploreLinkedServer(branchContext, nextServer, nextLocalLogin, branchChain, branchVisited, currentDepth + 1);
                }
            }
            catch (Exception ex)
            {
                // Log the error so we can see why exploration failed
                Logger.TraceNested($"Failed to explore {targetServer}: {ex.Message}");
            }
        }

        private static DataTable QueryLinkedServers(DatabaseContext databaseContext)
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
AND srv.provider = 'SQLNCLI'
ORDER BY srv.name;";

            return databaseContext.QueryService.ExecuteTable(query);
        }
    }
}
