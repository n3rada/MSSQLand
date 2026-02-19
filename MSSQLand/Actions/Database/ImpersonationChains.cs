// MSSQLand/Actions/Database/ImpersonationChains.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace MSSQLand.Actions.Database
{
    internal class ImpersonationChains : BaseAction
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
            BuildImpersonationChains(databaseContext, startingLogin, new List<string>(), allChains, new HashSet<string>(), 0);

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

        private void BuildImpersonationChains(DatabaseContext databaseContext, string startingLogin,
            List<string> currentPath, List<ImpersonationChain> allChains, HashSet<string> visited, int depth)
        {
            const int maxDepth = 10;
            if (depth >= maxDepth) return;

            // Get logins that can be impersonated from current context
            string query = @"
SELECT sp.name
FROM sys.server_principals sp
WHERE HAS_PERMS_BY_NAME(sp.name, 'LOGIN', 'IMPERSONATE') = 1
    AND sp.type_desc IN ('SQL_LOGIN', 'WINDOWS_LOGIN')
    AND sp.name NOT LIKE '##%'
    AND sp.name != SYSTEM_USER
ORDER BY sp.name;";

            DataTable impersonatableLogins = databaseContext.QueryService.ExecuteTable(query);

            if (impersonatableLogins.Rows.Count == 0)
                return;

            foreach (DataRow row in impersonatableLogins.Rows)
            {
                string loginToImpersonate = row["name"].ToString();

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

                // Impersonate the login and recurse
                try
                {
                    databaseContext.QueryService.ExecuteNonProcessing($"EXECUTE AS LOGIN = '{loginToImpersonate.Replace("'", "''")}';");

                    // Recursively find more chains
                    BuildImpersonationChains(databaseContext, startingLogin, newPath, allChains, newVisited, depth + 1);

                    // Revert impersonation
                    databaseContext.QueryService.ExecuteNonProcessing("REVERT;");
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to impersonate '{loginToImpersonate}': {ex.Message}");
                    // Try to revert just in case
                    try { databaseContext.QueryService.ExecuteNonProcessing("REVERT;"); } catch { }
                }
            }
        }
    }
}
