using MSSQLand.Services;
using System;
using System.Linq;
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
        /// Remote Procedure Call (RPC) usage
        /// RPC must be enabled for the linked server for EXEC AT queries
        /// </summary>
        public bool UseRemoteProcedureCall { get; set; } = true;

        /// <summary>
        /// Initializes the linked server chain and server names.
        /// </summary>
        /// <param name="serverChain">The server chain to initialize.</param>
        public LinkedServers(Server[] serverChain)
        {
            ServerChain = serverChain ?? throw new ArgumentNullException(nameof(serverChain));

            // Initialize ComputableServerNames starting with "0" as the convention.
            ComputableServerNames = new string[serverChain.Length + 1];
            ComputableServerNames[0] = "0";

            ComputableImpersonationNames = new string[serverChain.Length];

            ServerNames = new string[serverChain.Length];

            for (int i = 0; i < serverChain.Length; i++)
            {
                if (!string.IsNullOrEmpty(serverChain[i].Hostname))
                {
                    ComputableServerNames[i + 1] = serverChain[i].Hostname;
                    ServerNames[i] = serverChain[i].Hostname;
                }

                if (!string.IsNullOrEmpty(serverChain[i].ImpersonationUser))
                {
                    ComputableImpersonationNames[i] = serverChain[i].ImpersonationUser;
                } else
                {
                    ComputableImpersonationNames[i] = "";
                }
            }
        }

        /// <summary>
        /// Initializes the linked server chain using a comma-separated list of server names.
        /// </summary>
        /// <param name="serverList">Comma-separated list of server names (e.g., "SQL27,SQL53").</param>
        public LinkedServers(string serverList)
        {
            if (string.IsNullOrWhiteSpace(serverList))
            {
                throw new ArgumentException("Server list cannot be null or empty.", nameof(serverList));
            }

            // Split the server names and trim whitespace
            string[] serverNames = serverList.Split(',')
                                             .Select(name => name.Trim())
                                             .Where(name => !string.IsNullOrEmpty(name))
                                             .ToArray();

            // Initialize ServerChain with parsed server names
            ServerChain = serverNames.Select(name => new Server { Hostname = name }).ToArray();

            // Initialize ComputableServerNames starting with "0" as the convention
            ComputableServerNames = new string[ServerChain.Length + 1];
            ComputableServerNames[0] = "0";

            ComputableImpersonationNames = new string[ServerChain.Length];
            ServerNames = new string[ServerChain.Length];

            for (int i = 0; i < ServerChain.Length; i++)
            {
                ComputableServerNames[i + 1] = ServerChain[i].Hostname;
                ServerNames[i] = ServerChain[i].Hostname;
                ComputableImpersonationNames[i] = ""; // No impersonation user in this constructor
            }
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

    }

}
