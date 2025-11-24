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
        /// A computed array of database names extracted from the server chain.
        /// Expectation of {"master", "appdb", "analytics", ... }
        /// </summary>
        private string[] ComputableDatabaseNames { get; set; }

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
            ComputableDatabaseNames = new string[ServerChain.Length];
            ServerNames = new string[ServerChain.Length];

            for (int i = 0; i < ServerChain.Length; i++)
            {
                ComputableServerNames[i + 1] = ServerChain[i].Hostname;
                ServerNames[i] = ServerChain[i].Hostname;
                ComputableImpersonationNames[i] = ServerChain[i].ImpersonationUser ?? "";
                ComputableDatabaseNames[i] = ServerChain[i].Database ?? "";
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
                    ImpersonationUser = server.ImpersonationUser,
                    Database = server.Database
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
                string database = ServerChain[i].Database;

                StringBuilder part = new StringBuilder(serverName);

                // Add user@database or just @database
                if (!string.IsNullOrEmpty(impersonationUser) && !string.IsNullOrEmpty(database))
                {
                    part.Append($":{impersonationUser}@{database}");
                }
                else if (!string.IsNullOrEmpty(impersonationUser))
                {
                    part.Append($":{impersonationUser}");
                }
                else if (!string.IsNullOrEmpty(database))
                {
                    part.Append($"@{database}");
                }

                chainParts.Add(part.ToString());
            }

            return chainParts;
        }


        /// <summary>
        /// Adds a new server to the linked server chain.
        /// If the chain is empty, it creates a new LinkedServers instance.
        /// </summary>
        /// <param name="newServer">The hostname of the new linked server.</param>
        /// <param name="impersonationUser">Optional impersonation user.</param>
        /// <param name="database">Optional database context.</param>
        public void AddToChain(string newServer, string? impersonationUser = null, string? database = null)
        {
            Logger.Debug($"Adding server {newServer} to the linked server chain.");

            if (string.IsNullOrWhiteSpace(newServer))
            {
                throw new ArgumentException("Server name cannot be null or empty.", nameof(newServer));
            }

            List<Server> updatedChain = ServerChain.ToList();
            updatedChain.Add(new Server 
            { 
                Hostname = newServer, 
                ImpersonationUser = impersonationUser,
                Database = database
            });

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
                linkedImpersonation: ComputableImpersonationNames,
                linkedDatabases: ComputableDatabaseNames
             );
        }

        /// <summary>
        /// Recursively constructs a nested `OPENQUERY` statement for querying linked SQL servers.
        /// </summary>
        /// <param name="linkedServers">An array of server names representing the path of linked servers to traverse. '0' in front of them is mandatory to make the query work properly.</param>
        /// <param name="query">The SQL query to be executed at the final server in the linked server path.</param>
        /// <param name="thicksCounter">A counter used to double the single quotes for each level of nesting.</param>
        /// <param name="linkedImpersonation">An array of impersonation users for each server.</param>
        /// <param name="linkedDatabases">An array of database contexts for each server.</param>
        /// <returns>A string containing the nested `OPENQUERY` statement.</returns>
        private string BuildSelectOpenQueryChainRecursive(string[] linkedServers, string query, int thicksCounter = 0, string[] linkedImpersonation = null, string[] linkedDatabases = null)
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

            // Prepare the database context, if any
            string database = null;
            if (linkedDatabases != null && linkedDatabases.Length > 0)
            {
                database = linkedDatabases[0];
                // Create a new array without the first element
                linkedDatabases = linkedDatabases.Skip(1).ToArray();
            }


            string thicksRepr = new('\'', (int)Math.Pow(2, thicksCounter));

            // Base case: if this is the last server in the chain
            if (linkedServers.Length == 1)
            {
                StringBuilder baseQuery = new StringBuilder();

                if (!string.IsNullOrEmpty(login))
                {
                    baseQuery.Append($"EXECUTE AS LOGIN = '{login}';");
                }

                // Add USE statement for linked servers that specify a database
                if (!string.IsNullOrEmpty(database))
                {
                    baseQuery.Append($"USE [{database}];");
                }

                baseQuery.Append(currentQuery.TrimEnd(';'));
                baseQuery.Append(";");

                currentQuery = baseQuery.ToString().Replace("'", thicksRepr);

                return currentQuery;
            }

            // Construct the OPENQUERY statement for the next server in the chain
            StringBuilder stringBuilder = new();
            stringBuilder.Append("SELECT * FROM OPENQUERY(");

            // Taking the next server in the path.
            stringBuilder.Append($"[{linkedServers[1]}],");

            
            stringBuilder.Append(thicksRepr);

            // We are now inside the query, on the linked server

            // Add impersonation if applicable
            if (!string.IsNullOrEmpty(login))
            {
                string impersonationQuery = $"EXECUTE AS LOGIN = '{login}';";
                stringBuilder.Append(impersonationQuery.Replace("'", new('\'', (int)Math.Pow(2, thicksCounter + 1))));
            }

            // Add database context if applicable
            if (!string.IsNullOrEmpty(database))
            {
                string useQuery = $"USE [{database}];";
                stringBuilder.Append(useQuery.Replace("'", new('\'', (int)Math.Pow(2, thicksCounter + 1))));
            }


            // Recursive call for the remaining servers
            string recursiveCall = BuildSelectOpenQueryChainRecursive(
                linkedServers: linkedServers.Skip(1).ToArray(), // Skip the current server
                linkedImpersonation: linkedImpersonation,
                linkedDatabases: linkedDatabases,
                query: currentQuery,
                thicksCounter: thicksCounter + 1
             );

            stringBuilder.Append(recursiveCall);

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
                linkedImpersonation: ComputableImpersonationNames,
                linkedDatabases: ComputableDatabaseNames
             );
        }

        /// <summary>
        /// Recursively constructs a nested EXEC AT statement for querying linked SQL servers.
        /// <param name="linkedServers">An array of server names.</param>
        /// <param name="query">The SQL query to execute.</param>
        /// <param name="linkedImpersonation">An array of impersonation users for each server.</param>
        /// <param name="linkedDatabases">An array of database contexts for each server.</param>
        /// <returns>A string containing the nested EXEC AT statement.</returns>
        private static string BuildRemoteProcedureCallRecursive(string[] linkedServers, string query, string[] linkedImpersonation = null, string[] linkedDatabases = null)
        {
            string currentQuery = query;

            // Start from the end of the array and skip the first element ("0")
            for (int i = linkedServers.Length - 1; i > 0; i--)
            {
                string server = linkedServers[i];
                StringBuilder queryBuilder = new StringBuilder();
         
                if (linkedImpersonation != null && linkedImpersonation.Length > 0)
                {
                    string login = linkedImpersonation[i-1];
                    if (!string.IsNullOrEmpty(login))
                    {
                        queryBuilder.Append($"EXECUTE AS LOGIN = '{login}';");
                    }
                }

                // Add USE statement for all linked servers (skip index 0 which is the direct connection)
                // Index mapping: linkedDatabases[0] corresponds to linkedServers[1] (first linked server)
                if (i > 1 && linkedDatabases != null && linkedDatabases.Length > 0)
                {
                    string database = linkedDatabases[i-1];
                    if (!string.IsNullOrEmpty(database))
                    {
                        queryBuilder.Append($"USE [{database}];");
                    }
                }

                queryBuilder.Append(currentQuery.TrimEnd(';'));
                queryBuilder.Append(";");
                    
                // Double single quotes to escape them in the SQL string
                currentQuery = $"EXEC ('{queryBuilder.ToString().Replace("'", "''")}') AT [{server}]";
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
            ComputableDatabaseNames = new string[ServerChain.Length];
            ServerNames = new string[ServerChain.Length];

            for (int i = 0; i < ServerChain.Length; i++)
            {
                ComputableServerNames[i + 1] = ServerChain[i].Hostname;
                ServerNames[i] = ServerChain[i].Hostname;
                ComputableImpersonationNames[i] = ServerChain[i].ImpersonationUser ?? "";
                ComputableDatabaseNames[i] = ServerChain[i].Database ?? "";
            }
        }

    }

}
