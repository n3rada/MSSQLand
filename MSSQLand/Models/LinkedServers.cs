// MSSQLand/Models/LinkedServers.cs

using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using System.Text;

namespace MSSQLand.Models
{
    /// <summary>
    /// Manages linked server chains for SQL Server connections.
    ///
    /// Syntax:
    /// - Semicolon (;) - Separates servers in the chain
    /// - Forward slash (/) - Specifies user to impersonate ("execute as user")
    /// - At sign (@) - Specifies database context
    /// - Brackets [...] - Used to protect the server name from being split by our delimiters
    ///
    /// Chain Format:
    /// - Servers are separated by semicolons: server1;server2;server3
    /// - Format for each server: hostname[:port][/user][@database]
    /// - Use brackets [] for hostnames containing any delimiter (: / @ ;)
    ///
    /// Examples:
    /// - Simple: SQL01;SQL02;SQL03
    /// - With users: SQL01/admin;SQL02/webapp@mydb
    /// - With port: SQL01:1433;SQL02:1434/admin
    /// - Hostname with delimiters: [SQL02;PROD];[SQL03/TEST];[SQL04@INST]
    /// - Complex: SQL01:1433/admin;[SQL02;PROD]/domain_user@db_name;SQL03
    /// - Bracketed with modifiers: [SERVER;PROD]:1434/admin@clients
    ///
    /// Note: Brackets protect only the hostname from delimiter interpretation.
    /// Port/user/database modifiers are specified after the closing bracket.
    /// Brackets are only needed when the hostname itself contains : / @ ; characters.
    /// </summary>
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
        /// A computed array of impersonation user arrays extracted from the server chain.
        /// Each entry is an array of users to impersonate in sequence on that server.
        /// Expectation: [["user1", "user2"], [], ["user3"], ...]
        /// </summary>
        public string[][] ComputableImpersonationUsers { get; set; }

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
        /// Set of linked server names known to not support RPC.
        /// Used to build hybrid RPC/OPENQUERY chains where only specific hops use OPENQUERY.
        /// </summary>
        private readonly HashSet<string> _nonRpcServers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Tracks servers for which the impersonation-via-OPENQUERY incompatibility
        /// warning has already been shown. Prevents repeated warnings across queries.
        /// </summary>
        private readonly HashSet<string> _openQueryImpersonationWarned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Returns true if any server in the chain has been marked as non-RPC capable.
        /// </summary>
        public bool HasNonRpcServers => _nonRpcServers.Count > 0;

        /// <summary>
        /// Returns true if all servers in the chain are marked as non-RPC capable.
        /// </summary>
        public bool AllServersNonRpc => ServerChain.Length > 0 && ServerNames.All(s => _nonRpcServers.Contains(s));

        /// <summary>
        /// Marks a specific linked server as not supporting RPC.
        /// Future queries will use OPENQUERY for this specific hop while maintaining RPC for others.
        /// </summary>
        /// <param name="serverName">The linked server name (as it appears in error messages or sys.servers).</param>
        public void MarkServerAsNonRpc(string serverName)
        {
            if (!string.IsNullOrEmpty(serverName))
            {
                _nonRpcServers.Add(serverName);
            }
        }

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

            ComputableImpersonationUsers = new string[ServerChain.Length][];
            ComputableDatabaseNames = new string[ServerChain.Length];
            ServerNames = new string[ServerChain.Length];

            for (int i = 0; i < ServerChain.Length; i++)
            {
                // Use QueryRoutingName for query building (prefers LinkedServerAlias if set)
                ComputableServerNames[i + 1] = ServerChain[i].QueryRoutingName;
                ServerNames[i] = ServerChain[i].QueryRoutingName;
                ComputableImpersonationUsers[i] = ServerChain[i].ImpersonationUsers ?? Array.Empty<string>();
                ComputableDatabaseNames[i] = ServerChain[i].Database ?? "";
            }
        }

        /// <summary>
        /// Initializes the linked server chain from a semicolon-separated list.
        /// </summary>
        /// <param name="chainInput">Semicolon-separated list of server names with optional impersonation users.</param>
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
                    LinkedServerAlias = server.LinkedServerAlias,
                    ImpersonationUsers = server.ImpersonationUsers,
                    Database = server.Database
                }).ToArray();
            }

            // Preserve RPC state from the original
            UseRemoteProcedureCall = original.UseRemoteProcedureCall;
            foreach (var server in original._nonRpcServers)
            {
                _nonRpcServers.Add(server);
            }

            RecomputeChain();
        }



        public string GetChainArguments()
        {
            return string.Join(";", GetChainParts());
        }

        /// <summary>
        /// Returns a properly formatted linked server chain string.
        /// Automatically adds brackets around hostnames containing any delimiter (: / @ ;).
        ///
        /// Examples:
        /// - SQL01, SQL02 → ["SQL01", "SQL02"]
        /// - SQL02;PROD → ["[SQL02;PROD]"]
        /// - SQL02/PROD → ["[SQL02/PROD]"]
        /// - SQL02@PROD → ["[SQL02@PROD]"]
        /// - SQL02:PROD → ["[SQL02:PROD]"]
        /// - [SQL02;PROD]/admin → ["[SQL02;PROD]/admin"] (brackets protect hostname only)
        /// </summary>
        public List<string> GetChainParts()
        {
            List<string> chainParts = new();

            for (int i = 0; i < ServerChain.Length; i++)
            {
                // Use QueryRoutingName (alias) for display - this is what user needs to type
                string serverName = ServerChain[i].QueryRoutingName;
                string[] impersonationUsers = ServerChain[i].ImpersonationUsers;
                string database = ServerChain[i].Database;

                // Bracket the hostname if it contains ANY delimiter character
                serverName = Misc.BracketIdentifier(serverName);

                StringBuilder part = new StringBuilder(serverName);

                // Add cascading impersonation users /user1/user2/user3
                if (impersonationUsers != null && impersonationUsers.Length > 0)
                {
                    foreach (var user in impersonationUsers)
                    {
                        part.Append($"/{user}");
                    }
                }

                // Add database
                if (!string.IsNullOrEmpty(database))
                {
                    part.Append($"@{database}");
                }

                chainParts.Add(part.ToString());
            }

            return chainParts;
        }

        /// <summary>
        /// Builds a formatted chain display string showing the full server path with impersonation.
        /// Impersonation on intermediate servers is shown as part of the connector arrow,
        /// making it clear which identity is used for each hop.
        /// Impersonation on the last server is shown as the execution context.
        ///
        /// Examples:
        /// - No impersonation:              SQL01 ──> SQL02 ──> SQL03
        /// - With login:                    SQL01 (sa) ──> SQL02 ──> SQL03
        /// - Initial host cascade:          SQL01 (test) ─(lowpriv → midpriv)─> SQL02 ──> SQL03
        /// - Intermediate impersonation:    SQL01 ──> SQL02 ─(user02)─> SQL03
        /// - Last server impersonation:     SQL01 ──> SQL02 ──> SQL03 (as webapp)
        /// - Last server cascade:           SQL01 ──> SQL02 ──> SQL03 (as test_user → log_user)
        /// - Mixed cascade:                 SQL01 (sa) ─(user02)─> SQL02 ──> SQL03 ─(test_user → log_user)─> SQL04
        /// </summary>
        /// <param name="initialHost">The initial host server name.</param>
        /// <param name="initialLogin">Optional login name shown on the initial host.</param>
        /// <param name="initialImpersonation">Optional impersonation users on the initial host.</param>
        /// <returns>A formatted chain display string.</returns>
        public string FormatChainDisplay(string initialHost, string initialLogin = null, string[] initialImpersonation = null)
        {
            StringBuilder sb = new();
            sb.Append(initialHost);

            if (!string.IsNullOrEmpty(initialLogin))
            {
                sb.Append($" ({initialLogin})");
            }

            // Initial host impersonation becomes the connector to the first linked server
            sb.Append(FormatConnector(initialImpersonation));

            for (int i = 0; i < ServerChain.Length; i++)
            {
                var server = ServerChain[i];

                sb.Append(server.QueryRoutingName);

                bool isLast = i == ServerChain.Length - 1;
                string[] users = server.ImpersonationUsers;
                bool hasImpersonation = users != null && users.Length > 0;

                if (isLast)
                {
                    // Last server: impersonation is the execution context, not a hop
                    if (hasImpersonation)
                    {
                        string cascade = string.Join(" → ", users);
                        sb.Append($" (as {cascade})");
                    }
                }
                else
                {
                    // Intermediate server: impersonation is shown as part of the connector
                    sb.Append(FormatConnector(users));
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Formats a connector arrow between servers, optionally including impersonation cascade.
        /// </summary>
        private static string FormatConnector(string[] impersonationUsers)
        {
            if (impersonationUsers != null && impersonationUsers.Length > 0)
            {
                string cascade = string.Join(" → ", impersonationUsers);
                return $" ─({cascade})─> ";
            }

            return " ──> ";
        }


        /// <summary>
        /// Adds a new server to the linked server chain.
        /// If the chain is empty, it creates a new LinkedServers instance.
        /// </summary>
        /// <param name="newServer">The hostname of the new linked server.</param>
        /// <param name="impersonationUsers">Optional array of impersonation users for cascading.</param>
        /// <param name="database">Optional database context.</param>
        public void AddToChain(string newServer, string[]? impersonationUsers = null, string? database = null)
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
                ImpersonationUsers = impersonationUsers,
                Database = database
            });

            ServerChain = updatedChain.ToArray();

            // Recompute arrays
            RecomputeChain();
        }

        /// <summary>
        /// Removes the last server from the linked server chain.
        /// This is the inverse of AddToChain, used for backtracking during exploration.
        /// </summary>
        public void RemoveLastFromChain()
        {
            if (ServerChain.Length == 0)
            {
                Logger.Debug("Cannot remove from an empty linked server chain.");
                return;
            }

            Logger.Debug($"Removing server {ServerChain.Last().Hostname} from the linked server chain.");
            ServerChain = ServerChain.Take(ServerChain.Length - 1).ToArray();
            RecomputeChain();
        }

        /// <summary>
        /// Parses a semicolon-separated list of servers into an array of Server objects.
        ///
        /// Syntax:
        /// - Semicolon (;) - Separates servers in the chain
        /// - Forward slash (/) - Specifies user to impersonate ("execute as user")
        /// - At sign (@) - Specifies database context
        /// - Brackets [...] - Used to protect the server name from being split by our delimiters
        ///
        /// Format: "server1;server2;server3"
        /// Each server: hostname[:port][/user][@database]
        ///
        /// Examples:
        /// - "SQL27/user01;SQL53/user02"
        /// - "SQL27:1433/user01@db;SQL53/user02"
        /// - "[SQL02;PROD];[SQL03/TEST];[SQL04@INST]/admin@mydb"
        /// - "[SQL02:8080]/domain_user@db_name;SQL03" (brackets protect : in hostname)
        /// - "[SERVER;PROD]:1434/admin@clients;SQL03" (bracketed hostname with port, user, and database)
        ///
        /// Note: Brackets protect only the hostname. Port/user/database modifiers come after the closing bracket.
        /// </summary>
        /// <param name="chainInput">Semicolon-separated list of servers.</param>
        /// <returns>An array of Server objects.</returns>
        private static Server[] ParseServerChain(string chainInput)
        {
            if (string.IsNullOrWhiteSpace(chainInput))
            {
                throw new ArgumentException("Server list cannot be null or empty.", nameof(chainInput));
            }

            // Split the input by semicolons, handling bracketed entries
            // Note: when parsing the individual servers in the chain, we set parsingPort to false
            // because ports are not expected in linked server chains.
            List<string> serverStrings = new List<string>();
            int pos = 0;

            while (pos < chainInput.Length)
            {
                // Skip whitespace
                while (pos < chainInput.Length && char.IsWhiteSpace(chainInput[pos]))
                    pos++;

                if (pos >= chainInput.Length)
                    break;

                string serverString;

                // Check if this entry is bracketed
                if (chainInput[pos] == '[')
                {
                    int closingBracket = chainInput.IndexOf(']', pos);
                    if (closingBracket == -1)
                        throw new ArgumentException($"Unclosed bracket in server chain at position {pos}");

                    // Extract content between brackets (this is the hostname)
                    string bracketedHostname = chainInput.Substring(pos + 1, closingBracket - pos - 1);
                    pos = closingBracket + 1;

                    // Continue extracting modifiers (port/user/database) after the bracket until semicolon
                    int semicolon = chainInput.IndexOf(';', pos);
                    string modifiers;
                    if (semicolon == -1)
                    {
                        modifiers = chainInput.Substring(pos);
                        pos = chainInput.Length;
                    }
                    else
                    {
                        modifiers = chainInput.Substring(pos, semicolon - pos);
                        pos = semicolon + 1;
                    }

                    // Combine bracketed hostname with modifiers for parsing
                    serverString = bracketedHostname + modifiers;
                }
                else
                {
                    // Find next semicolon
                    int semicolon = chainInput.IndexOf(';', pos);
                    if (semicolon == -1)
                    {
                        serverString = chainInput.Substring(pos);
                        pos = chainInput.Length;
                    }
                    else
                    {
                        serverString = chainInput.Substring(pos, semicolon - pos);
                        pos = semicolon + 1;
                    }
                }

                serverStrings.Add(serverString.Trim());
            }

            return serverStrings.Select(s => Server.ParseServer(s)).ToArray();
        }

        /// <summary>
        /// Constructs a nested `OPENQUERY` statement for querying linked SQL servers in a chain.
        /// It passes the query string as-is to the linked server without attempting to parse or validate it as T-SQL on the local server.
        /// https://learn.microsoft.com/en-us/sql/t-sql/functions/openquery-transact-sql
        ///
        /// </summary>
        /// <param name="query">The SQL query to execute at the final server.</param>
        /// <returns>A string containing the nested `OPENQUERY` statement.</returns>
        public string BuildSelectOpenQueryChain(string query)
        {
            return BuildSelectOpenQueryChainRecursive(
                linkedServers: ComputableServerNames,
                query: query,
                linkedImpersonation: ComputableImpersonationUsers,
                linkedDatabases: ComputableDatabaseNames
             );
        }

        /// <summary>
        /// Recursively constructs a nested `OPENQUERY` statement for querying linked SQL servers.
        /// Executes as a remote SELECT engine on the linked server.
        /// Each level doubles the single quotes to escape them properly.
        /// </summary>
        /// <param name="linkedServers">An array of server names representing the path of linked servers to traverse. '0' in front of them is mandatory to make the query work properly.</param>
        /// <param name="query">The SQL query to be executed at the final server in the linked server path.</param>
        /// <param name="thicksCounter">A counter used to double the single quotes for each level of nesting.</param>
        /// <param name="linkedImpersonation">An array of impersonation user arrays for each server.</param>
        /// <param name="linkedDatabases">An array of database contexts for each server.</param>
        /// <returns>A string containing the nested `OPENQUERY` statement.</returns>
        private string BuildSelectOpenQueryChainRecursive(string[] linkedServers, string query, int thicksCounter = 0, string[][] linkedImpersonation = null, string[] linkedDatabases = null)
        {
            if (linkedServers == null || linkedServers.Length == 0)
            {
                throw new ArgumentException("linkedServers cannot be null or empty.", nameof(linkedServers));
            }

            string currentQuery = query;


            // Prepare the cascading impersonation users, if any
            string[] impersonationUsers = null;
            if (linkedImpersonation != null && linkedImpersonation.Length > 0)
            {
                impersonationUsers = linkedImpersonation[0];
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


            string thicksRepr = new('\'',(1 << thicksCounter));

            // Base case: if this is the last server in the chain
            if (linkedServers.Length == 1)
            {
                StringBuilder baseQuery = new StringBuilder();

                // Skip impersonation — this content will be wrapped inside the parent's
                // OPENQUERY, and EXECUTE AS LOGIN inside OPENQUERY always fails because
                // sp_describe_first_result_set cannot handle it during metadata probing.
                if (impersonationUsers != null && impersonationUsers.Length > 0)
                {
                    if (_openQueryImpersonationWarned.Add(linkedServers[0]))
                    {
                        string users = string.Join(" \u2192 ", impersonationUsers);
                        Logger.Warning($"Impersonation ({users}) skipped on '{linkedServers[0]}': EXECUTE AS LOGIN is incompatible with OPENQUERY. Enable RPC on this linked server for impersonation support.");
                    }
                }

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

            // Skip impersonation — EXECUTE AS LOGIN inside OPENQUERY always fails
            // because sp_describe_first_result_set cannot handle it during metadata probing.
            if (impersonationUsers != null && impersonationUsers.Length > 0)
            {
                if (_openQueryImpersonationWarned.Add(linkedServers[1]))
                {
                    string users = string.Join(" \u2192 ", impersonationUsers);
                    Logger.Warning($"Impersonation ({users}) skipped on '{linkedServers[1]}': EXECUTE AS LOGIN is incompatible with OPENQUERY. Enable RPC on this linked server for impersonation support.");
                }
            }

            // Add database context if applicable
            if (!string.IsNullOrEmpty(database))
            {
                string useQuery = $"USE [{database}];";
                stringBuilder.Append(useQuery.Replace("'", new('\'',(1 << (thicksCounter + 1)))));
            }


            // Recursive call for the remaining servers
            string recursiveCall = BuildSelectOpenQueryChainRecursive(
                linkedServers: linkedServers.Skip(1).ToArray(),
                linkedImpersonation: linkedImpersonation,
                linkedDatabases: linkedDatabases,
                query: currentQuery,
                thicksCounter: thicksCounter + 1
             );

            stringBuilder.Append(recursiveCall);

            // Closing the remote request
            stringBuilder.Append(thicksRepr);
            stringBuilder.Append(")");;


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
                linkedImpersonation: ComputableImpersonationUsers,
                linkedDatabases: ComputableDatabaseNames
             );
        }

        /// <summary>
        /// Recursively constructs a nested EXEC AT statement for querying linked SQL servers.
        /// It loop from innermost server to outermost server.
        /// Each iteration adds impersonation and database context if provided. Then, appends prior query escaped.
        /// And finally wraps everything in EXEC ('...') AT [server].
        ///
        /// Big-O time complexity of O(n * L) where:
        ///     n = number of linked servers
        ///     L = final query string length
        /// This is expected and optimal: you must touch the whole string each time because SQL must be re-encoded at each hop.
        /// <param name="linkedServers">An array of server names.</param>
        /// <param name="query">The SQL query to execute.</param>
        /// <param name="linkedImpersonation">An array of impersonation user arrays for each server.</param>
        /// <param name="linkedDatabases">An array of database contexts for each server.</param>
        /// <returns>A string containing the nested EXEC AT statement.</returns>
        private static string BuildRemoteProcedureCallRecursive(string[] linkedServers, string query, string[][] linkedImpersonation = null, string[] linkedDatabases = null)
        {
            string currentQuery = query;

            // Start from the end of the array and skip the first element ("0")
            for (int i = linkedServers.Length - 1; i > 0; i--)
            {
                string server = linkedServers[i];
                StringBuilder queryBuilder = new StringBuilder();

                // Add cascading impersonation
                if (linkedImpersonation != null && linkedImpersonation.Length > 0)
                {
                    string[] impersonationUsers = linkedImpersonation[i-1];
                    if (impersonationUsers != null && impersonationUsers.Length > 0)
                    {
                        foreach (var user in impersonationUsers)
                        {
                            queryBuilder.Append($"EXECUTE AS LOGIN = '{user}'; ");
                        }
                    }
                }

                if (linkedDatabases != null && linkedDatabases.Length > 0)
                {
                    string database = linkedDatabases[i-1];
                    if (!string.IsNullOrEmpty(database) && database != "master")
                    {
                        queryBuilder.Append($"USE [{database}]; ");
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
        /// Builds a hybrid chain that uses RPC (EXEC AT) for capable servers
        /// and OPENQUERY for servers known to lack RPC support.
        ///
        /// This preserves identity propagation and impersonation on RPC-capable hops
        /// while falling back to OPENQUERY only where strictly necessary.
        ///
        /// Example with chain [SQL02, SQL01] where SQL01 lacks RPC:
        ///   EXEC ('EXECUTE AS LOGIN = ''dev''; SELECT * FROM OPENQUERY([SQL01], ''query'')') AT [SQL02]
        /// </summary>
        /// <param name="query">The SQL query to execute at the final server.</param>
        /// <returns>A hybrid RPC/OPENQUERY chain query.</returns>
        public string BuildHybridChain(string query)
        {
            return BuildHybridChainIterative(
                linkedServers: ComputableServerNames,
                query: query,
                linkedImpersonation: ComputableImpersonationUsers,
                linkedDatabases: ComputableDatabaseNames
            );
        }

        /// <summary>
        /// Iteratively constructs a hybrid chain from innermost server to outermost.
        /// At each hop, decides between EXEC AT (RPC) and OPENQUERY based on _nonRpcServers.
        /// The escaping logic (doubling single quotes per nesting level) is identical for both.
        /// </summary>
        private string BuildHybridChainIterative(
            string[] linkedServers,
            string query,
            string[][] linkedImpersonation = null,
            string[] linkedDatabases = null)
        {
            string currentQuery = query;

            // Build from innermost server to outermost, skipping the "0" sentinel at index 0
            for (int i = linkedServers.Length - 1; i > 0; i--)
            {
                string server = linkedServers[i];
                bool useRpc = !_nonRpcServers.Contains(server);

                StringBuilder queryBuilder = new StringBuilder();

                // Add cascading impersonation
                if (linkedImpersonation != null && linkedImpersonation.Length > 0)
                {
                    string[] impersonationUsers = linkedImpersonation[i - 1];
                    if (impersonationUsers != null && impersonationUsers.Length > 0)
                    {
                        if (!useRpc)
                        {
                            // EXECUTE AS LOGIN inside OPENQUERY always fails because
                            // sp_describe_first_result_set cannot handle it during metadata probing.
                            // Skip impersonation for this hop entirely.
                            if (_openQueryImpersonationWarned.Add(server))
                            {
                                string users = string.Join(" \u2192 ", impersonationUsers);
                                Logger.Warning($"Impersonation ({users}) skipped on '{server}': EXECUTE AS LOGIN is incompatible with OPENQUERY. Enable RPC on this linked server for impersonation support.");
                            }
                        }
                        else
                        {
                            foreach (var user in impersonationUsers)
                            {
                                queryBuilder.Append($"EXECUTE AS LOGIN = '{user}'; ");
                            }
                        }
                    }
                }

                // Add database context
                if (linkedDatabases != null && linkedDatabases.Length > 0)
                {
                    string database = linkedDatabases[i - 1];
                    if (!string.IsNullOrEmpty(database) && database != "master")
                    {
                        queryBuilder.Append($"USE [{database}]; ");
                    }
                }

                queryBuilder.Append(currentQuery.TrimEnd(';'));
                queryBuilder.Append(";");

                // Double single quotes for the nesting level
                string escaped = queryBuilder.ToString().Replace("'", "''");

                if (useRpc)
                {
                    currentQuery = $"EXEC ('{escaped}') AT [{server}]";
                }
                else
                {
                    currentQuery = $"SELECT * FROM OPENQUERY([{server}],'{escaped}')";
                }
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

            ComputableImpersonationUsers = new string[ServerChain.Length][];
            ComputableDatabaseNames = new string[ServerChain.Length];
            ServerNames = new string[ServerChain.Length];

            for (int i = 0; i < ServerChain.Length; i++)
            {
                // Use QueryRoutingName for query building (prefers LinkedServerAlias if set)
                ComputableServerNames[i + 1] = ServerChain[i].QueryRoutingName;
                ServerNames[i] = ServerChain[i].QueryRoutingName;
                ComputableImpersonationUsers[i] = ServerChain[i].ImpersonationUsers ?? Array.Empty<string>();
                ComputableDatabaseNames[i] = ServerChain[i].Database ?? "";
            }
        }

    }

}
