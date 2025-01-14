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
        /// Remote Procedure Call (RPC)
        /// RPC must be enabled for the linked server for EXEC AT queries
        /// </summary>
        public bool SupportRemoteProcedureCall { get; set; } = true;

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
        /// Constructs a nested `OPENQUERY` statement for querying linked SQL servers in a chain.
        /// </summary>
        /// <param name="query">The SQL query to execute at the final server.</param>
        /// <returns>A string containing the nested `OPENQUERY` statement.</returns>
        public string BuildOpenQueryChain(string query)
        {
            return BuildOpenQueryChainRecursive(
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
        /// <param name="ticks">A counter used to double the single quotes for each level of nesting.</param>
        /// <returns>A string containing the nested `OPENQUERY` statement.</returns>
        private string BuildOpenQueryChainRecursive(string[] linkedServers, string query, int ticks = 0, string[] linkedImpersonation = null)
        {
            if (linkedServers == null || linkedServers.Length == 0)
            {
                throw new ArgumentException("linkedServers cannot be null or empty.", nameof(linkedServers));
            }

            string currentQuery = query;

            // Prepare the impersonation login, if any
            string login = null;
            if (linkedImpersonation != null || linkedImpersonation.Length > 0)
            {
                login = linkedImpersonation[0];
                // Create a new array without the first element
                linkedImpersonation = linkedImpersonation.Skip(1).ToArray();
            }

            // Base case: if this is the last server in the chain
            if (linkedServers.Length == 1)
            {

                if (!string.IsNullOrEmpty(login))
                {
                    currentQuery = $"EXECUTE AS LOGIN = '{login}'; {currentQuery.TrimEnd(';')}; REVERT;";
                }

                currentQuery = currentQuery.Replace("'", new string('\'', (int)Math.Pow(2, ticks)));

                return currentQuery;
            }

            // Construct the OPENQUERY statement for the next server in the chain
            StringBuilder stringBuilder = new();
            stringBuilder.Append("SELECT * FROM OPENQUERY(");

            // Taking the next server in the path.
            stringBuilder.Append($"\"{linkedServers[1]}\", ");

            string numberOfTicks = new('\'', (int)Math.Pow(2, ticks));
            stringBuilder.Append(numberOfTicks);

            // We are now inside the query, on the linked server

            // Add impersonation if applicable
            if (!string.IsNullOrEmpty(login))
            {
                stringBuilder.Append($"EXECUTE AS LOGIN = '{login}';");
            }


            // Recursive call for the remaining servers
            string recursiveCall = BuildOpenQueryChainRecursive(
                linkedServers: linkedServers.Skip(1).ToArray(), // Skip the current server
                linkedImpersonation: linkedImpersonation,
                query: currentQuery,
                ticks: ticks + 1
             );

            stringBuilder.Append(recursiveCall);

            // Add REVERT if impersonation was applied
            if (!string.IsNullOrEmpty(login))
            {
                stringBuilder.Append(" REVERT;");
            }

            // Closing the remote request
            stringBuilder.Append(numberOfTicks);
            stringBuilder.Append(")");

            return stringBuilder.ToString();
        }


        public string BuildRemoteProcedureCallChain(string query)
        {
            return BuildRemoteProcedureCallRecursive(
                linkedServers: ComputableServerNames,
                query: query,
                linkedImpersonation: ComputableImpersonationNames
             );
        }

        /// <summary>
        /// The BuildRemoteProcedureCallRecursive method constructs a nested EXEC AT statement for querying linked SQL servers in a chain.
        /// </summary>
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
                currentQuery = $"EXEC ('{currentQuery.Replace("'", "''")}') AT {server}";
            }

            return currentQuery;
        }

    }

}
