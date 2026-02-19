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
        private const int MaxDepth = 10;

        private const string ImpersonatableLoginsQuery = @"
SELECT sp.name
FROM sys.server_principals sp
WHERE HAS_PERMS_BY_NAME(sp.name, 'LOGIN', 'IMPERSONATE') = 1
    AND sp.type_desc IN ('SQL_LOGIN', 'WINDOWS_LOGIN')
    AND sp.name NOT LIKE '##%'
ORDER BY sp.name;";

        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Starting impersonation chain mapping");

            string startingLogin = databaseContext.UserService.SystemUser;

            if (databaseContext.UserService.IsAdmin())
            {
                Logger.Success("Current user can impersonate any login directly (no chain mapping needed)");
                return new DataTable();
            }

            var allChains = new List<(string StartingLogin, List<string> Path)>();

            BuildImpersonationMap(databaseContext, startingLogin, new List<string>(), allChains, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            if (allChains.Count == 0)
            {
                Logger.Warning("No impersonation chains found from current user");
                return new DataTable();
            }

            DataTable result = FormatResults(allChains);

            if (!Logger.IsSilentModeEnabled)
            {
                Console.WriteLine(OutputFormatter.ConvertDataTable(result));
            }

            Logger.Success($"Found {allChains.Count} impersonation chain(s)");

            return result;
        }

        private void BuildImpersonationMap(DatabaseContext databaseContext, string startingLogin,
            List<string> currentPath, List<(string StartingLogin, List<string> Path)> allChains,
            HashSet<string> visited, int depth = 0)
        {
            if (depth >= MaxDepth) return;

            Logger.Trace($"Exploring depth={depth}, path=[{string.Join(" -> ", currentPath)}]");

            bool isLinkedServer = !databaseContext.QueryService.LinkedServers.IsEmpty;

            if (isLinkedServer)
            {
                Logger.Trace($"Querying linked server with impersonation: [{(databaseContext.QueryService.ExecutionServer.ImpersonationUsers != null ? string.Join(" -> ", databaseContext.QueryService.ExecutionServer.ImpersonationUsers) : "none")}]");
            }

            DataTable impersonatableLogins = databaseContext.QueryService.ExecuteTable(ImpersonatableLoginsQuery);

            if (impersonatableLogins.Rows.Count == 0) return;

            List<string> logins = impersonatableLogins.AsEnumerable()
                .Select(r => r["name"].ToString())
                .ToList();

            Logger.Debug($"Found {logins.Count} impersonatable login(s) at depth {depth}: [{string.Join(", ", logins)}]");

            foreach (string loginToImpersonate in logins)
            {
                // Skip self-impersonation and cycles
                if (loginToImpersonate.Equals(startingLogin, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (visited.Contains(loginToImpersonate))
                    continue;

                var newPath = new List<string>(currentPath) { loginToImpersonate };
                allChains.Add((startingLogin, new List<string>(newPath)));

                var newVisited = new HashSet<string>(visited, StringComparer.OrdinalIgnoreCase) { loginToImpersonate };

                // Impersonate and recurse to find deeper chains
                string[] savedImpersonations = null;

                try
                {
                    Logger.Debug($"Impersonating '{loginToImpersonate}' to explore deeper chains");

                    if (isLinkedServer)
                    {
                        savedImpersonations = PushLinkedImpersonation(databaseContext, loginToImpersonate);
                    }
                    else
                    {
                        databaseContext.QueryService.ExecuteNonProcessing($"EXECUTE AS LOGIN = '{loginToImpersonate.Replace("'", "''")}';");
                    }

                    BuildImpersonationMap(databaseContext, startingLogin, newPath, allChains, newVisited, depth + 1);
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to impersonate '{loginToImpersonate}': {ex.Message}");
                }
                finally
                {
                    // Always restore impersonation state
                    try
                    {
                        if (isLinkedServer)
                        {
                            RestoreLinkedImpersonation(databaseContext, savedImpersonations);
                        }
                        else
                        {
                            databaseContext.QueryService.ExecuteNonProcessing("REVERT;");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Trace($"Failed to revert impersonation: {ex.Message}");
                    }

                    Logger.Debug($"Reverted from '{loginToImpersonate}'");
                }
            }
        }

        /// <summary>
        /// Pushes a new login onto the linked server impersonation chain.
        /// Returns the previous impersonation array for later restoration.
        /// </summary>
        private static string[] PushLinkedImpersonation(DatabaseContext databaseContext, string login)
        {
            string[] previous = databaseContext.QueryService.ExecutionServer.ImpersonationUsers;

            var updated = new List<string>();
            if (previous != null)
            {
                updated.AddRange(previous);
            }
            updated.Add(login);

            string[] updatedArray = updated.ToArray();

            databaseContext.QueryService.ExecutionServer.ImpersonationUsers = updatedArray;

            int lastServerIndex = databaseContext.QueryService.LinkedServers.ServerChain.Length - 1;
            if (lastServerIndex >= 0)
            {
                databaseContext.QueryService.LinkedServers.ComputableImpersonationUsers[lastServerIndex] = updatedArray;
            }

            Logger.Trace($"Impersonation chain: [{string.Join(", ", updated)}]");

            return previous;
        }

        /// <summary>
        /// Restores the linked server impersonation chain to a previous state.
        /// </summary>
        private static void RestoreLinkedImpersonation(DatabaseContext databaseContext, string[] previous)
        {
            databaseContext.QueryService.ExecutionServer.ImpersonationUsers = previous;

            int lastServerIndex = databaseContext.QueryService.LinkedServers.ServerChain.Length - 1;
            if (lastServerIndex >= 0)
            {
                databaseContext.QueryService.LinkedServers.ComputableImpersonationUsers[lastServerIndex] = previous ?? Array.Empty<string>();
            }

            Logger.Trace($"Restored chain to: [{(previous != null ? string.Join(", ", previous) : "none")}]");
        }

        /// <summary>
        /// Formats discovered impersonation chains into a presentable DataTable.
        /// </summary>
        private static DataTable FormatResults(List<(string StartingLogin, List<string> Path)> chains)
        {
            DataTable result = new DataTable();
            result.Columns.Add("Starting Login", typeof(string));
            result.Columns.Add("Middle Logins", typeof(string));
            result.Columns.Add("End Login", typeof(string));
            result.Columns.Add("Hops", typeof(int));

            foreach (var (startingLogin, path) in chains.OrderBy(c => c.Path.Count).ThenBy(c => string.Join(" -> ", c.Path)))
            {
                string endLogin = path.Last();
                string middleLogins = path.Count > 1
                    ? string.Join(", ", path.Take(path.Count - 1))
                    : "";

                result.Rows.Add(startingLogin, middleLogins, endLogin, path.Count);
            }

            return result;
        }
    }
}
