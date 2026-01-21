// MSSQLand/Actions/Remote/LinkMap.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text;

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
            public List<string> NonSqlLinks { get; set; } = new();
            public List<ServerNode> Children { get; set; } = new();
            
            public bool IsPrivileged => IsSysadmin || MappedUser.Equals("dbo", StringComparison.OrdinalIgnoreCase);
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

            // Create root node representing the starting server
            _rootNode = new ServerNode
            {
                Alias = databaseContext.Server.Hostname,
                ActualName = databaseContext.Server.Hostname,
                LoggedInUser = databaseContext.Server.SystemUser,
                MappedUser = databaseContext.Server.MappedUser,
                ImpersonatedUser = null,
                IsSysadmin = false // We don't check this for the starting server
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

            // Compute starting server's state hash for loop detection
            ServerExecutionState startingState = ServerExecutionState.FromContext(
                databaseContext.Server.Hostname, 
                databaseContext.UserService
            );
            string startingHash = startingState.GetStateHash();

            // Mark starting server as explored
            _globallyExploredServers.Add(databaseContext.Server.Hostname);

            // Explore each linked server from root
            foreach (DataRow row in sqlServerLinks)
            {
                string remoteServer = row["Link"].ToString();
                string localLogin = row["Local Login"] == DBNull.Value || string.IsNullOrEmpty(row["Local Login"].ToString()) 
                    ? null 
                    : row["Local Login"].ToString();

                // Skip if already explored (shouldn't happen at root level, but be safe)
                if (_globallyExploredServers.Contains(remoteServer))
                {
                    Logger.TraceNested($"Server '{remoteServer}' already explored. Skipping.");
                    continue;
                }

                HashSet<string> visitedInChain = new() { startingHash };
                DatabaseContext tempContext = databaseContext.Copy();
                List<ServerNode> currentPath = new();
                
                ExploreLinkedServer(tempContext, remoteServer, localLogin, _rootNode, currentPath, visitedInChain, currentDepth: 0);
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
        /// </summary>
        private void ExploreLinkedServer(DatabaseContext databaseContext, string targetServer, string requiredLocalLogin,
            ServerNode parentNode, List<ServerNode> currentPath, HashSet<string> visitedInChain, int currentDepth)
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
                ServerExecutionState currentState = ServerExecutionState.FromContext(targetServer, databaseContext.UserService);
                string stateHash = currentState.GetStateHash();

                // Check for loop in THIS chain path only
                if (visitedInChain.Contains(stateHash))
                {
                    Logger.TraceNested($"Loop detected at server '{targetServer}' with user '{currentState.SystemUser}'. Skipping to prevent infinite recursion.");
                    return;
                }

                visitedInChain.Add(stateHash);

                // Create node for this server
                var currentNode = new ServerNode
                {
                    Alias = targetServer,
                    ActualName = actualServerName,
                    LoggedInUser = currentState.SystemUser,
                    MappedUser = currentState.MappedUser,
                    ImpersonatedUser = impersonatedUser,
                    IsSysadmin = currentState.IsSysadmin
                };

                // Add to parent's children
                parentNode.Children.Add(currentNode);

                // Track the path for chain commands
                var newPath = new List<ServerNode>(currentPath) { currentNode };
                _allChains.Add(newPath);

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

                // Store non-SQL linked servers
                if (remoteOtherLinks.Count > 0)
                {
                    Logger.TraceNested($"Non-SQL linked servers on '{targetServer}':");
                    foreach (DataRow row in remoteOtherLinks)
                    {
                        string name = row["Link"].ToString();
                        string provider = row["Provider"].ToString();
                        currentNode.NonSqlLinks.Add($"{name} ({provider})");
                        Logger.TraceNested($"{name} ({provider})");
                    }
                }

                Logger.TraceNested($"Exploring SQL Server links on '{targetServer}' (found {remoteSqlLinks.Count})");

                // Mark this server as globally explored
                _globallyExploredServers.Add(targetServer);

                foreach (DataRow row in remoteSqlLinks)
                {
                    string nextServer = row["Link"].ToString();
                    string nextLocalLogin = row["Local Login"] == DBNull.Value || string.IsNullOrEmpty(row["Local Login"].ToString())
                        ? null
                        : row["Local Login"].ToString();

                    // Skip if already explored globally
                    if (_globallyExploredServers.Contains(nextServer))
                    {
                        Logger.TraceNested($"Server '{nextServer}' already explored via another path. Skipping.");
                        continue;
                    }

                    // Create copies for this branch
                    HashSet<string> branchVisited = new(visitedInChain);
                    DatabaseContext branchContext = databaseContext.Copy();

                    // Explore recursively
                    ExploreLinkedServer(branchContext, nextServer, nextLocalLogin, currentNode, newPath, branchVisited, currentDepth + 1);
                }
            }
            catch (Exception ex)
            {
                Logger.TraceNested($"Failed to explore {targetServer}: {ex.Message}");
            }
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
            Console.WriteLine($"{_rootNode.Alias} ({_rootNode.LoggedInUser} [{_rootNode.MappedUser}])");
            
            if (_rootNode.NonSqlLinks.Count > 0)
            {
                Console.WriteLine($"    [OPENQUERY: {string.Join(", ", _rootNode.NonSqlLinks)}]");
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
            string connector = isLast ? "└─► " : "├─► ";
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

            // Privilege marker
            string privilegeMarker = node.IsPrivileged ? " ★" : "";

            // Build the main line with chain command
            Console.WriteLine($"{indent}{connector}{displayName} ({node.LoggedInUser} [{node.MappedUser}]){privilegeMarker}");
            Console.WriteLine($"{childIndent}► -l {chainCommand}");

            // Show non-SQL links if any
            if (node.NonSqlLinks.Count > 0)
            {
                Console.WriteLine($"{childIndent}[OPENQUERY: {string.Join(", ", node.NonSqlLinks)}]");
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
                if (lastNode.IsPrivileged)
                    privilegedChains.Add(chain);
                else
                    standardChains.Add(chain);
            }

            if (privilegedChains.Count > 0)
            {
                Logger.Success($"Privileged chains ({privilegedChains.Count}) - sysadmin or dbo at endpoint:");
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
                    ImpersonationUser = node.ImpersonatedUser,
                    Database = null
                });
            }

            var lastNode = chain[chain.Count - 1];
            string endpoint = lastNode.Alias;
            if (!lastNode.Alias.Equals(lastNode.ActualName, StringComparison.OrdinalIgnoreCase))
            {
                endpoint = $"{lastNode.Alias} [{lastNode.ActualName}]";
            }

            string privilegeMarker = lastNode.IsPrivileged ? " ★" : "";
            
            LinkedServers linkedServers = new LinkedServers(serverList.ToArray());
            string chainArg = linkedServers.GetChainArguments();
            
            Logger.InfoNested($"{endpoint}{privilegeMarker}: -l {chainArg}");
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
