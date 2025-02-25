using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using System.Text;

namespace MSSQLand.Models
{
    public class LinkedServers
    {
        /// <summary>
        /// An array of linked servers in the server chain.
        /// </summary>
        public Server[] ServerChain { get; set; }

        /// <summary>
        /// A computed array of server names extracted from the server chain.
        /// Expectation of {"0", "SQL02", "SQL03", "SQL04", ... }
        /// </summary>
        private string[] ComputableServerNames { get; set; }

        /// <summary>
        /// A computed array of impersonation names extracted from the server chain.
        /// Expectation of {"webapp03", "", "webapp05", ... }
        /// </summary>
        private string[] ComputableImpersonationNames { get; set; }

        /// <summary>
        /// A public array of server names extracted from the server chain.
        /// Expectation of {"SQL02", "SQL03", "SQL04", ... }
        /// </summary>
        public string[] ServerNames { get; private set; }

        /// <summary>
        /// Determines if the linked server chain is empty.
        /// </summary>
        public bool IsEmpty => ServerChain.Length == 0;

        /// <summary>
        /// Remote Procedure Call (RPC) usage
        /// RPC must be enabled for the linked server for EXEC AT queries
        /// </summary>
        public bool UseRemoteProcedureCall { get; set; } = true;

        /// <summary>
        /// Initializes an empty linked server chain.
        /// This allows a default empty state where no linked servers exist.
        /// </summary>
        public LinkedServers() : this(Array.Empty<Server>()) { }


        /// <summary>
        /// Initializes the linked server chain and computes necessary structures.
        /// </summary>
        /// <param name="serverChain">The array of servers forming the linked chain.</param>
        public LinkedServers(Server[] serverChain)
        {
            ServerChain = serverChain ?? Array.Empty<Server>(); // Ensure it's never null

            // Initialize ComputableServerNames starting with "0" as the convention.
            ComputableServerNames = new string[ServerChain.Length + 1];
            ComputableServerNames[0] = "0";

            ComputableImpersonationNames = new string[ServerChain.Length];
            ServerNames = new string[ServerChain.Length];

            for (int i = 0; i < ServerChain.Length; i++)
            {
                ComputableServerNames[i + 1] = ServerChain[i].Hostname;
                ServerNames[i] = ServerChain[i].Hostname;
                ComputableImpersonationNames[i] = ServerChain[i].ImpersonationUser ?? "";
            }
        }

        /// <summary>
        /// Initializes the linked server chain from a comma-separated list.
        /// </summary>
        /// <param name="chainInput">Comma-separated list of server names with optional impersonation users.</param>
        public LinkedServers(string chainInput)
            : this(string.IsNullOrWhiteSpace(chainInput) ? Array.Empty<Server>() : ParseServerChain(chainInput))
        { }

        public LinkedServers(LinkedServers original)
        {
            if (original == null || original.ServerChain == null)
            {
                ServerChain = Array.Empty<Server>();
            }
            else
            {
                ServerChain = original.ServerChain.Select(server => new Server
                {
                    Hostname = server.Hostname,
                    ImpersonationUser = server.ImpersonationUser
                }).ToArray();
            }

            RecomputeChain();
        }



        public string GetChainArguments()
        {
            return string.Join(",", GetChainParts());
        }

        /// <summary>
        /// Returns a properly formatted linked server chain string.
        /// </summary>
        public List<string> GetChainParts()
        {
            List<string> chainParts = new();

            for (int i = 0; i < ServerChain.Length; i++)
            {
                string serverName = ServerChain[i].Hostname;
                string impersonationUser = ServerChain[i].ImpersonationUser;

                if (!string.IsNullOrEmpty(impersonationUser))
                {
                    chainParts.Add($"{serverName}:{impersonationUser}");
                }
                else
                {
                    chainParts.Add(serverName);
                }
            }

            return chainParts;
        }


        /// <summary>
        /// Adds a new server to the linked server chain.
        /// If the chain is empty, it creates a new LinkedServers instance.
        /// </summary>
        /// <param name="newServer">The hostname of the new linked server.</param>
        /// <param name="impersonationUser">Optional impersonation user.</param>
        public void AddToChain(string newServer, string? impersonationUser = null)
        {
            Logger.Debug($"Adding server {newServer} to the linked server chain.");

            if (string.IsNullOrWhiteSpace(newServer))
            {
                throw new ArgumentException("Server name cannot be null or empty.", nameof(newServer));
            }

            List<Server> updatedChain = ServerChain.ToList();
            updatedChain.Add(new Server { Hostname = newServer, ImpersonationUser = impersonationUser });

            ServerChain = updatedChain.ToArray();

            // Recompute arrays
            RecomputeChain();
        }

        /// <summary>
        /// Parses a comma-separated list of servers into an array of Server objects.
        /// Accepts the format "SQL27:user01,SQL53:user02".
        /// </summary>
        /// <param name="chainInput">Comma-separated list of servers.</param>
        /// <returns>An array of Server objects.</returns>
        private static Server[] ParseServerChain(string chainInput)
        {
            if (string.IsNullOrWhiteSpace(chainInput))
            {
                throw new ArgumentException("Server list cannot be null or empty.", nameof(chainInput));
            }

            return chainInput.Split(',')
                             .Select(serverString => Server.ParseServer(serverString.Trim()))
                             .ToArray();
        }

        /// <summary>
        /// Constructs a nested `OPENQUERY` statement for querying linked SQL servers in a chain.
        /// It passes the query string as-is to the linked server without attempting to parse or validate it as T-SQL on the local server.
        /// https://learn.microsoft.com/en-us/sql/t-sql/functions/openquery-transact-sql
        /// </summary>
        /// <param name="query">The SQL query to execute at the final server.</param>
        /// <returns>A string containing the nested `OPENQUERY` statement.</returns>
        public string BuildSelectOpenQueryChain(string query)
        {
            return BuildSelectOpenQueryChainRecursive(
                linkedServers: ComputableServerNames,
                query: query,
                linkedImpersonation: ComputableImpersonationNames
             );
        }

        /// <summary>
        /// Recursively constructs a nested `OPENQUERY` statement for querying linked SQL servers.
        /// </summary>
        /// <param name="linkedServers">An array of server names representing the path of linked servers to traverse. '0' in front of them is mandatory to make the query work properly.</param>
        /// <param name="query">The SQL query to be executed at the final server in the linked server path.</param>
        /// <param name="thicksCounter">A counter used to double the single quotes for each level of nesting.</param>
        /// <returns>A string containing the nested `OPENQUERY` statement.</returns>
        private string BuildSelectOpenQueryChainRecursive(string[] linkedServers, string query, int thicksCounter = 0, string[] linkedImpersonation = null)
        {
            if (linkedServers == null || linkedServers.Length == 0)
            {
                throw new ArgumentException("linkedServers cannot be null or empty.", nameof(linkedServers));
            }

            string currentQuery = query;
            

            // Prepare the impersonation login, if any
            string login = null;
            if (linkedImpersonation != null && linkedImpersonation.Length > 0)
            {

                login = linkedImpersonation[0];
                // Create a new array without the first element
                linkedImpersonation = linkedImpersonation.Skip(1).ToArray();
            }


            string thicksRepr = new('\'', (int)Math.Pow(2, thicksCounter));

            // Base case: if this is the last server in the chain
            if (linkedServers.Length == 1)
            {

                if (!string.IsNullOrEmpty(login))
                {
                    currentQuery = $"EXECUTE AS LOGIN = '{login}'; {currentQuery.TrimEnd(';')}; REVERT;";
                }

                currentQuery = currentQuery.Replace("'", thicksRepr);

                return currentQuery;
            }

            // Construct the OPENQUERY statement for the next server in the chain
            StringBuilder stringBuilder = new();
            stringBuilder.Append("SELECT * FROM OPENQUERY(");

            // Taking the next server in the path.
            stringBuilder.Append($"[{linkedServers[1]}], ");

            
            stringBuilder.Append(thicksRepr);

            // We are now inside the query, on the linked server

            // Add impersonation if applicable
            if (!string.IsNullOrEmpty(login))
            {
                string impersonationQuery = $"EXECUTE AS LOGIN = '{login}'; ";
                stringBuilder.Append(impersonationQuery.Replace("'", new('\'', (int)Math.Pow(2, thicksCounter + 1))));
            }


            // Recursive call for the remaining servers
            string recursiveCall = BuildSelectOpenQueryChainRecursive(
                linkedServers: linkedServers.Skip(1).ToArray(), // Skip the current server
                linkedImpersonation: linkedImpersonation,
                query: currentQuery,
                thicksCounter: thicksCounter + 1
             );

            stringBuilder.Append(recursiveCall);

            // Add REVERT if impersonation was applied
            if (!string.IsNullOrEmpty(login))
            {
                stringBuilder.Append(" REVERT;");
            }

            // Closing the remote request
            stringBuilder.Append(thicksRepr);
            stringBuilder.Append(")");


            return stringBuilder.ToString();
        }

        /// <summary>
        /// The BuildRemoteProcedureCallRecursive method constructs a nested EXEC AT statement for querying linked SQL servers in a chain.
        /// When you use EXEC to run a query on a linked server, SQL Server expects the query to be valid T-SQL.
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        public string BuildRemoteProcedureCallChain(string query)
        {
            return BuildRemoteProcedureCallRecursive(
                linkedServers: ComputableServerNames,
                query: query,
                linkedImpersonation: ComputableImpersonationNames
             );
        }

        /// <summary>
        /// Recursively constructs a nested EXEC AT statement for querying linked SQL servers.
        /// <param name="linkedServers"></param>
        /// <param name="query"></param>
        /// <returns></returns>
        private static string BuildRemoteProcedureCallRecursive(string[] linkedServers, string query, string[] linkedImpersonation = null)
        {
            string currentQuery = query;

            // Start from the end of the array and skip the first element ("0")
            for (int i = linkedServers.Length - 1; i > 0; i--)
            {
                string server = linkedServers[i];
         
                if (linkedImpersonation != null || linkedImpersonation.Length > 0)
                {
                    string login = linkedImpersonation[i-1];
                    if (!string.IsNullOrEmpty(login))
                    {
                        currentQuery = $"EXECUTE AS LOGIN = '{login}'; {currentQuery.TrimEnd(';')}; REVERT;";
                    }
                    
                }
                    
                // Double single quotes to escape them in the SQL string
                currentQuery = $"EXEC ('{currentQuery.Replace("'", "''")} ') AT [{server}]";
            }

            return currentQuery;
        }


        /// <summary>
        /// Recomputes the internal arrays (server names, impersonation users).
        /// </summary>
        private void RecomputeChain()
        {
            ComputableServerNames = new string[ServerChain.Length + 1];
            ComputableServerNames[0] = "0";

            ComputableImpersonationNames = new string[ServerChain.Length];
            ServerNames = new string[ServerChain.Length];

            for (int i = 0; i < ServerChain.Length; i++)
            {
                ComputableServerNames[i + 1] = ServerChain[i].Hostname;
                ServerNames[i] = ServerChain[i].Hostname;
                ComputableImpersonationNames[i] = ServerChain[i].ImpersonationUser ?? "";
            }
        }

    }

}
