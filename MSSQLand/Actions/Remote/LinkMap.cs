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

            DataTable allLinkedServers = QueryAllLinkedServers(databaseContext);

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

            // Show non-SQL linked servers at initial server
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

            // Compute starting server's state hash for loop detection
            // This prevents chains that loop back to the starting point
            ServerExecutionState startingState = ServerExecutionState.FromContext(
                databaseContext.Server.Hostname, 
                databaseContext.UserService
            );
            string startingHash = startingState.GetStateHash();

            // Start exploration from each linked server
            foreach (DataRow row in sqlServerLinks)
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

            // Separate chains by privilege level at final server
            var privilegedChains = new List<List<Dictionary<string, string>>>();
            var standardChains = new List<List<Dictionary<string, string>>>();

            foreach (var chain in _discoveredChains)
            {
                if (chain.Count == 0) continue;
                
                // Check if privileged at the last hop (sysadmin OR mapped to dbo)
                var lastEntry = chain[chain.Count - 1];
                bool isSysadminAtEnd = lastEntry.ContainsKey("IsSysadmin") && 
                                       lastEntry["IsSysadmin"].Equals("True", StringComparison.OrdinalIgnoreCase);
                bool isDboAtEnd = lastEntry.ContainsKey("Mapped") &&
                                  lastEntry["Mapped"].Equals("dbo", StringComparison.OrdinalIgnoreCase);
                
                if (isSysadminAtEnd || isDboAtEnd)
                    privilegedChains.Add(chain);
                else
                    standardChains.Add(chain);
            }

            // Display privileged chains first
            if (privilegedChains.Count > 0)
            {
                Logger.NewLine();
                Logger.Success($"Privileged paths ({privilegedChains.Count}) - sysadmin or dbo at final server:");
                DisplayChains(privilegedChains, initialServerEntry, isPrivileged: true);
            }

            // Display standard chains
            if (standardChains.Count > 0)
            {
                Logger.NewLine();
                Logger.Info($"Standard paths ({standardChains.Count}):");
                DisplayChains(standardChains, initialServerEntry, isPrivileged: false);
            }

            return _discoveredChains;
        }

        private void DisplayChains(List<List<Dictionary<string, string>>> chains, string initialServerEntry, bool isPrivileged)
        {
            foreach (var chain in chains)
            {
                if (chain.Count == 0) continue;

                List<string> formattedLines = new() { initialServerEntry };
                List<Server> serverChainList = new();

                foreach (var entry in chain)
                {
                    string serverName = entry["ServerName"];
                    string actualName = entry.ContainsKey("ActualServerName") ? entry["ActualServerName"] : serverName;
                    string loggedIn = entry["LoggedIn"];
                    string mapped = entry["Mapped"];
                    string impersonatedUser = entry.ContainsKey("ImpersonatedUser") ? entry["ImpersonatedUser"].Trim() : "-";

                    // Display format: show actual name in brackets if different from alias
                    string displayName = serverName;
                    if (!serverName.Equals(actualName, StringComparison.OrdinalIgnoreCase))
                    {
                        displayName = $"{serverName} [{actualName}]";
                    }

                    // Add privileged indicator (sysadmin or dbo)
                    bool isSysadmin = entry.ContainsKey("IsSysadmin") && 
                                      entry["IsSysadmin"].Equals("True", StringComparison.OrdinalIgnoreCase);
                    bool isDbo = mapped.Equals("dbo", StringComparison.OrdinalIgnoreCase);
                    string privilegeMarker = (isSysadmin || isDbo) ? " ★" : "";

                    formattedLines.Add($"-{(impersonatedUser != "-" ? $" {impersonatedUser} " : "-")}-> {displayName} ({loggedIn} [{mapped}]){privilegeMarker}");
                    
                    // Show non-SQL linked servers available at this node
                    if (entry.ContainsKey("NonSqlLinks") && !string.IsNullOrEmpty(entry["NonSqlLinks"]))
                    {
                        formattedLines.Add($"[OPENQUERY: {entry["NonSqlLinks"]}]");
                    }
                    
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
            if (currentDepth >= _limit)
            {
                Logger.TraceNested($"Limit {_limit} reached at server '{targetServer}'. Backtracking."); 
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

                // Query actual server name (@@SERVERNAME) - may differ from alias
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

                // Create state hash for loop detection (server + user context)
                // Use the ALIAS for loop detection since that's how we route queries
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
                    { "ActualServerName", actualServerName },
                    { "LoggedIn", currentState.SystemUser },
                    { "Mapped", currentState.MappedUser },
                    { "ImpersonatedUser", impersonatedUser ?? "-" },
                    { "IsSysadmin", currentState.IsSysadmin.ToString() }
                };
                currentChain.Add(chainEntry);

                // Save this chain (make a copy)
                _discoveredChains.Add(new List<Dictionary<string, string>>(currentChain));

                // Get linked servers from this remote server
                DataTable remoteLinkedServers;
                try
                {
                    remoteLinkedServers = QueryAllLinkedServers(databaseContext);
                }
                catch (Exception ex)
                {
                    Logger.TraceNested($"Failed to enumerate links on {targetServer}: {ex.Message}");
                    return;
                }

                // Separate SQL Server links from others
                var remoteSqlLinks = new List<DataRow>();
                var remoteOtherLinks = new List<DataRow>();
                
                foreach (DataRow row in remoteLinkedServers.Rows)
                {
                    string provider = row["Provider"].ToString();
                    if (provider.StartsWith("SQLNCLI") || provider.StartsWith("MSOLEDBSQL"))
                        remoteSqlLinks.Add(row);
                    else
                        remoteOtherLinks.Add(row);
                }

                // Log non-SQL linked servers found at this node
                if (remoteOtherLinks.Count > 0)
                {
                    Logger.TraceNested($"Non-SQL linked servers on '{targetServer}':");
                    foreach (DataRow row in remoteOtherLinks)
                    {
                        string name = row["Link"].ToString();
                        string provider = row["Provider"].ToString();
                        Logger.TraceNested($"{name} ({provider})");
                    }
                    
                    // Store non-SQL links in chain entry for display
                    var nonSqlLinks = new List<string>();
                    foreach (DataRow row in remoteOtherLinks)
                    {
                        nonSqlLinks.Add($"{row["Link"]} ({row["Provider"]})");
                    }
                    chainEntry["NonSqlLinks"] = string.Join(", ", nonSqlLinks);
                    // Update the saved chain with non-SQL links info
                    _discoveredChains[_discoveredChains.Count - 1] = new List<Dictionary<string, string>>(currentChain);
                }

                Logger.TraceNested($"Exploring SQL Server links on '{targetServer}' (found {remoteSqlLinks.Count})");

                foreach (DataRow row in remoteSqlLinks)
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

        private static DataTable QueryAllLinkedServers(DatabaseContext databaseContext)
        {
            string query = @"
SELECT 
    srv.name AS [Link], 
    srv.provider AS [Provider],
    srv.product AS [Product],
    srv.data_source AS [DataSource],
    prin.name AS [Local Login],
    ll.remote_name AS [Remote Login]
FROM master.sys.servers srv
LEFT JOIN master.sys.linked_logins ll 
    ON srv.server_id = ll.server_id
LEFT JOIN master.sys.server_principals prin 
    ON ll.local_principal_id = prin.principal_id
WHERE srv.is_linked = 1
ORDER BY srv.provider, srv.name;";

            return databaseContext.QueryService.ExecuteTable(query);
        }
    }
}
