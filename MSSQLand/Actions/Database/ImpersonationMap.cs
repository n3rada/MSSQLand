// MSSQLand/Actions/Database/ImpersonationMap.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace MSSQLand.Actions.Database
{
    internal class ImpersonationMap : BaseAction
    {
        private class ImpersonationChain
        {
            public string StartingLogin { get; set; }
            public List<string> ChainPath { get; set; }
            public int Hops { get; set; }

            public ImpersonationChain()
            {
                ChainPath = new List<string>();
            }
        }

        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Starting impersonation chain mapping");

            string startingLogin = databaseContext.UserService.SystemUser;

            // Check if user is sysadmin - they can impersonate anyone, so no need to map chains
            if (databaseContext.UserService.IsAdmin())
            {
                Logger.Success("Current user can impersonate any login directly (no chain mapping needed).");
                return new DataTable();
            }

            List<ImpersonationChain> allChains = new List<ImpersonationChain>();

            // Build chains recursively
            BuildImpersonationMap(databaseContext, startingLogin, new List<string>(), allChains, new HashSet<string>(), 0);

            if (allChains.Count == 0)
            {
                Logger.Warning("No impersonation chains found from current user.");
                return new DataTable();
            }

            // Format results
            DataTable result = new DataTable();
            result.Columns.Add("Starting Login", typeof(string));
            result.Columns.Add("Middle Logins", typeof(string));
            result.Columns.Add("End Login", typeof(string));
            result.Columns.Add("Hops", typeof(int));

            foreach (var chain in allChains.OrderBy(c => c.Hops).ThenBy(c => string.Join(" -> ", c.ChainPath)))
            {
                string middleLogins = "";
                string endLogin = "";

                if (chain.ChainPath.Count > 0)
                {
                    endLogin = chain.ChainPath.Last();

                    if (chain.ChainPath.Count > 1)
                    {
                        middleLogins = string.Join(", ", chain.ChainPath.Take(chain.ChainPath.Count - 1));
                    }
                }

                result.Rows.Add(chain.StartingLogin, middleLogins, endLogin, chain.Hops);
            }

            if (!Logger.IsSilentModeEnabled)
            {
                Console.WriteLine(OutputFormatter.ConvertDataTable(result));
            }

            Logger.Success($"Found {allChains.Count} impersonation chain(s)");

            return result;
        }

        private void BuildImpersonationMap(DatabaseContext databaseContext, string startingLogin,
            List<string> currentPath, List<ImpersonationChain> allChains, HashSet<string> visited, int depth)
        {
            const int maxDepth = 10;
            if (depth >= maxDepth) return;

            Logger.Trace($"BuildImpersonationMap: depth={depth}, currentPath=[{string.Join(" -> ", currentPath)}], visited=[{string.Join(", ", visited)}]");

            // Get logins that can be impersonated from current context
            string query = @"
SELECT sp.name
FROM sys.server_principals sp
WHERE HAS_PERMS_BY_NAME(sp.name, 'LOGIN', 'IMPERSONATE') = 1
    AND sp.type_desc IN ('SQL_LOGIN', 'WINDOWS_LOGIN')
    AND sp.name NOT LIKE '##%'
ORDER BY sp.name;";

            // Trace execution context before query
            bool isLinkedServer = !databaseContext.QueryService.LinkedServers.IsEmpty;
            if (isLinkedServer)
            {
                Logger.Trace($"About to execute query through linked server");
                Logger.Trace($"ExecutionServer.ImpersonationUsers before query: [{(databaseContext.QueryService.ExecutionServer.ImpersonationUsers != null ? string.Join(", ", databaseContext.QueryService.ExecutionServer.ImpersonationUsers) : "null")}]");
            }

            DataTable impersonatableLogins = databaseContext.QueryService.ExecuteTable(query);

            // Trace the actual logins found
            if (impersonatableLogins.Rows.Count > 0)
            {
                var foundLogins = new List<string>();
                foreach (DataRow row in impersonatableLogins.Rows)
                {
                    foundLogins.Add(row["name"].ToString());
                }
                Logger.Trace($"Query returned logins: [{string.Join(", ", foundLogins)}]");
            }
            else
            {
                Logger.Trace("Query returned no logins");
            }

            Logger.Debug($"Found {impersonatableLogins.Rows.Count} impersonatable login(s) at depth {depth}");

            if (impersonatableLogins.Rows.Count == 0)
                return;

            foreach (DataRow row in impersonatableLogins.Rows)
            {
                string loginToImpersonate = row["name"].ToString();

                // Skip self-impersonation back to the starting login
                if (loginToImpersonate.Equals(startingLogin, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Avoid cycles
                if (visited.Contains(loginToImpersonate))
                    continue;

                // Create a new chain path
                List<string> newPath = new List<string>(currentPath) { loginToImpersonate };

                // Add this chain
                allChains.Add(new ImpersonationChain
                {
                    StartingLogin = startingLogin,
                    ChainPath = new List<string>(newPath),
                    Hops = depth + 1
                });

                // Mark as visited for this branch
                HashSet<string> newVisited = new HashSet<string>(visited) { loginToImpersonate };

                // Check if we're executing through a linked server
                string[] previousImpersonations = null;

                // Impersonate the login and recurse
                try
                {
                    Logger.Debug($"Impersonating '{loginToImpersonate}' to explore deeper chains");

                    if (isLinkedServer)
                    {
                        // For linked servers, track impersonation in ExecutionServer
                        // so it's prepended to all subsequent queries
                        previousImpersonations = databaseContext.QueryService.ExecutionServer.ImpersonationUsers;

                        // Add new impersonation to the chain
                        var newImpersonations = new List<string>();
                        if (previousImpersonations != null)
                        {
                            newImpersonations.AddRange(previousImpersonations);
                        }
                        newImpersonations.Add(loginToImpersonate);

                        // Update ExecutionServer
                        databaseContext.QueryService.ExecutionServer.ImpersonationUsers = newImpersonations.ToArray();
                        Logger.Trace($"Set ExecutionServer.ImpersonationUsers to: [{string.Join(", ", newImpersonations)}]");

                        // Also update the LinkedServers ComputableImpersonationUsers cache
                        int lastServerIndex = databaseContext.QueryService.LinkedServers.ServerChain.Length - 1;
                        if (lastServerIndex >= 0)
                        {
                            databaseContext.QueryService.LinkedServers.ComputableImpersonationUsers[lastServerIndex] = newImpersonations.ToArray();
                        }

                        Logger.Trace($"Updated impersonation chain to: [{string.Join(", ", newImpersonations)}]");
                    }
                    else
                    {
                        // Direct connection: use EXECUTE AS
                        databaseContext.QueryService.ExecuteNonProcessing($"EXECUTE AS LOGIN = '{loginToImpersonate.Replace("'", "''")}';");
                    }

                    // Recursively find more chains
                    BuildImpersonationMap(databaseContext, startingLogin, newPath, allChains, newVisited, depth + 1);

                    // Revert impersonation
                    Logger.Debug($"Reverting from '{loginToImpersonate}'");

                    if (isLinkedServer)
                    {
                        // Restore previous impersonation chain
                        databaseContext.QueryService.ExecutionServer.ImpersonationUsers = previousImpersonations;

                        // Also restore ComputableImpersonationUsers cache
                        int lastServerIndex = databaseContext.QueryService.LinkedServers.ServerChain.Length - 1;
                        if (lastServerIndex >= 0)
                        {
                            databaseContext.QueryService.LinkedServers.ComputableImpersonationUsers[lastServerIndex] = previousImpersonations ?? Array.Empty<string>();
                        }

                        Logger.Trace($"Restored impersonation chain to: [{(previousImpersonations != null ? string.Join(", ", previousImpersonations) : "null")}]");
                    }
                    else
                    {
                        // Direct connection: use REVERT
                        databaseContext.QueryService.ExecuteNonProcessing("REVERT;");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to impersonate '{loginToImpersonate}': {ex.Message}");

                    // Try to revert/restore impersonation state
                    try
                    {
                        if (isLinkedServer)
                        {
                            databaseContext.QueryService.ExecutionServer.ImpersonationUsers = previousImpersonations;

                            // Restore ComputableImpersonationUsers cache
                            int lastServerIndex = databaseContext.QueryService.LinkedServers.ServerChain.Length - 1;
                            if (lastServerIndex >= 0)
                            {
                                databaseContext.QueryService.LinkedServers.ComputableImpersonationUsers[lastServerIndex] = previousImpersonations ?? Array.Empty<string>();
                            }
                        }
                        else
                        {
                            databaseContext.QueryService.ExecuteNonProcessing("REVERT;");
                        }
                    }
                    catch { }
                }
            }
        }
    }
}
