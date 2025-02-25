using MSSQLand.Actions.Database;
using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Data;

namespace MSSQLand.Actions.Network
{
    internal class LinkedServerExplorer : BaseAction
    {
        [ExcludeFromArguments]
        private readonly Dictionary<Guid, List<Dictionary<string, string>>> _serverMapping = new();

        public override void ValidateArguments(string additionalArguments)
        {
            // No additional arguments needed
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            
            Logger.Task("Enumerating linked servers");

            DataTable linkedServersTable = Links.GetLinkedServers(databaseContext);

            if (linkedServersTable.Rows.Count == 0)
            {
                Logger.Warning("No linked servers found.");
                return null;
            }

            // Step 2: Explore each linked server recursively
            Logger.Task("Exploring all possible linked server chains");

            foreach (DataRow row in linkedServersTable.Rows)
            {
                Logger.NewLine();
                string remoteServer = row["Link"].ToString();
                string localLogin = row["Local Login"].ToString();


                Guid chainId = Guid.NewGuid(); // Create a unique chain identifier.
                _serverMapping[chainId] = new List<Dictionary<string, string>>();

                ExploreServer(databaseContext.Clone(), remoteServer, localLogin, chainId);
            }



            // Step 3: Output final structured mapping
            Logger.NewLine();

            // Construct the initial server entry
            string initialServerEntry = $"{databaseContext.Server.Hostname} ({databaseContext.Server.SystemUser} [{databaseContext.Server.MappedUser}])";

            Logger.Debug($"Initial server entry: {initialServerEntry}");

            // If LinkedServers is not empty, include its chain representation
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

                for (int i = 0; i < chainMapping.Count; i++)
                {
                    string serverName = chainMapping[i]["ServerName"];
                    string loggedIn = chainMapping[i]["LoggedIn"];
                    string mapped = chainMapping[i]["Mapped"];
                    string impersonatedUser = chainMapping[i]["ImpersonatedUser"];

                    // Show impersonation transition
                    formattedLines.Add($"-{impersonatedUser}-> {serverName} ({loggedIn} [{mapped}])");

                }

                Console.WriteLine();
                Console.WriteLine(string.Join(" ", formattedLines));
            }

            return _serverMapping;
        }

        /// <summary>
        /// Recursively explores linked servers, ensuring proper user impersonation when needed.
        /// </summary>
        private void ExploreServer(DatabaseContext databaseContext, string targetServer, string expectedLocalLogin, Guid chainId)
        {
            Logger.Task($"Accessing linked server: {targetServer}");

            // Step 1: Check if we are already logged in with the correct user
            var (currentUser, systemUser) = databaseContext.UserService.GetInfo();
            Logger.TaskNested($"[{databaseContext.QueryService.ExecutionServer}] LoggedIn: {systemUser}, Mapped: {currentUser}");

            string impersonatedUser = null; // Track if we needed to impersonate

            if (systemUser != expectedLocalLogin)
            {
                Logger.TaskNested($"Current user '{systemUser}' does not match expected local login '{expectedLocalLogin}'");
                Logger.TaskNested("Attempting impersonation");

                if (databaseContext.UserService.CanImpersonate(expectedLocalLogin))
                {
                    databaseContext.UserService.ImpersonateUser(expectedLocalLogin);
                    impersonatedUser = expectedLocalLogin;
                    Logger.TaskNested($"[{databaseContext.QueryService.ExecutionServer}] Impersonated '{expectedLocalLogin}' to access {targetServer}.");
                }
                else
                {
                    Logger.Warning($"[{databaseContext.QueryService.ExecutionServer}] Cannot impersonate {expectedLocalLogin} on {targetServer}. Skipping.");
                    return;
                }
            }

            // Grow the linked server chain
            databaseContext.QueryService.LinkedServers.AddToChain(targetServer);
            databaseContext.QueryService.ExecutionServer = targetServer;

            var (mappedUser, remoteLoggedInUser) = databaseContext.UserService.GetInfo();

            if (!_serverMapping.ContainsKey(chainId))
            {
                _serverMapping[chainId] = new List<Dictionary<string, string>>();
            }

            // Generate a SHA-256 hash for the current mapping entry
            string entryHash = Misc.ComputeSHA256($"{targetServer}:{remoteLoggedInUser}:{mappedUser}:{impersonatedUser}");

            // Check if this mapping already exists, preventing infinite loops
            if (_serverMapping[chainId].Exists(entry => entry["Hash"] == entryHash))
            {
                Logger.Warning($"Detected potential loop at {targetServer}. Skipping to prevent recursion.");
                return;
            }

            Logger.Debug($"Adding mapping for {targetServer}");
            Logger.DebugNested($"LoggedIn User: {remoteLoggedInUser}");
            Logger.DebugNested($"Mapped User: {mappedUser}");
            Logger.DebugNested($"Impersonated User: {impersonatedUser}");

            _serverMapping[chainId].Add(new Dictionary<string, string>
            {
                { "ServerName", targetServer },
                { "LoggedIn", remoteLoggedInUser },
                { "Mapped", mappedUser },
                { "ImpersonatedUser", !string.IsNullOrEmpty(impersonatedUser) ? $" {impersonatedUser} " : "-" },
                { "Hash", entryHash }
            });


            Logger.TaskNested($"[{databaseContext.QueryService.ExecutionServer}] LoggedIn: {remoteLoggedInUser}, Mapped: {mappedUser}");

            // Step 4: Retrieve linked servers from remote server and continue recursion
            DataTable remoteLinkedServers = Links.GetLinkedServers(databaseContext);

            // Step 5: Build new chain and explore each linked server
            foreach (DataRow row in remoteLinkedServers.Rows)
            {
                string nextServer = row["Link"].ToString();
                string nextLocalLogin = row["Local Login"].ToString();

                ExploreServer(databaseContext, nextServer, nextLocalLogin, chainId);
            }
        }

    }
}   
