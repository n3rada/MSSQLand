using MSSQLand.Models;
using MSSQLand.Utilities;
using System;
using System.Collections.Concurrent;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace MSSQLand.Services
{
    public class QueryService
    {
        public readonly SqlConnection Connection;
        public string ExecutionServer { get; set; }

        private LinkedServers _linkedServers = new();
        
        private const int MAX_RETRIES = 3;

        /// <summary>
        /// Dictionary to cache Azure SQL detection for each execution server.
        /// </summary>
        private readonly ConcurrentDictionary<string, bool> _isAzureSQLCache = new();


        /// <summary>
        /// LinkedServers property. When set, updates the ExecutionServer to the last server in the ServerNames array.
        /// </summary>
        public LinkedServers LinkedServers
        {
            get => _linkedServers;
            set
            {
                _linkedServers = value ?? new LinkedServers();
                if (_linkedServers.ServerNames.Length > 0)
                {
                    ExecutionServer = _linkedServers.ServerNames.Last();
                    Logger.Debug($"Execution server set to: {ExecutionServer}");
                }
                else
                {
                    ExecutionServer = GetServerName();
                }
            }
        }

        public QueryService(SqlConnection connection)
        {
            Connection = connection;
            ExecutionServer = GetServerName();
        }

        private string GetServerName()
        {
            // SELECT SERVERPROPERTY('MachineName')
            // SELECT @@SERVERNAME
            string serverName = ExecuteScalar("SELECT @@SERVERNAME").ToString();

            if (serverName != null)
            {
                // Extract only the hostname before the backslash
                return serverName.Contains("\\") ? serverName.Split('\\')[0] : serverName;
            }

            return "Unknown";
        }


        /// <summary>
        /// Executes a SQL query against the database and returns a single scalar value.
        /// </summary>
        /// <param name="query">The SQL query to execute.</param>
        /// <returns>The SqlDataReader resulting from the query.</returns>
        public SqlDataReader Execute(string query)
        {
            return ExecuteWithHandling(query, executeReader: true) as SqlDataReader;
        }


        /// <summary>
        /// Executes a SQL query against the database without returning a result (e.g., for INSERT, UPDATE, DELETE).
        /// </summary>
        /// <param name="query">The SQL query to execute.</param>
        public int ExecuteNonProcessing(string query)
        {
            var result = ExecuteWithHandling(query, executeReader: false);
            if (result == null)
            {
                return -1; // Indicate an error
            }
            return (int)result;
        }

        /// <summary>
        /// Shared execution logic for handling SQL queries, with error handling for both Execute and ExecuteNonProcessing.
        /// </summary>
        /// <param name="query">The SQL query to execute.</param>
        /// <param name="executeReader">True to use ExecuteReader, false to use ExecuteNonQuery.</param>
        /// <param name="timeout">Initial timeout in seconds.</param>
        /// <param name="retryCount">Current retry attempt (for exponential backoff).</param>
        /// <returns>Result of ExecuteReader if executeReader is true; otherwise null.</returns>
        private object ExecuteWithHandling(string query, bool executeReader, int timeout = 120, int retryCount = 0)
        {
            if (string.IsNullOrEmpty(query))
            {
                throw new ArgumentException("Query cannot be null or empty.", nameof(query));
            }

            // Check if we've exceeded max retries
            if (retryCount > MAX_RETRIES)
            {
                Logger.Error($"Maximum retry attempts ({MAX_RETRIES}) exceeded. Aborting query execution.");
                return null;
            }
            
            if (Connection == null || Connection.State != ConnectionState.Open)
            {
                return null;
            }

            string finalQuery = PrepareQuery(query);

            try
            {

                using var command = new SqlCommand(finalQuery, Connection)
                {
                    CommandType = CommandType.Text,
                    CommandTimeout = timeout
                };

                if (executeReader)
                {
                    SqlDataReader reader = command.ExecuteReader();

                    return reader;
                }
                else
                {
                    return command.ExecuteNonQuery();
                }
            }
            catch (SqlException ex) when (ex.Message.Contains("Timeout"))
            {
                int newTimeout = timeout * 2; // Exponential backoff
                Logger.Warning($"Query timed out after {timeout} seconds. Retrying with {newTimeout} seconds (attempt {retryCount + 1}/{MAX_RETRIES})");
                return ExecuteWithHandling(query, executeReader, newTimeout, retryCount + 1);
            }
            catch (Exception ex)
            {
                Logger.Debug($"Query execution returned an error: {ex.Message}");

                if (ex.Message.Contains("not configured for RPC"))
                {
                    Logger.Warning("The targeted server is not configured for Remote Procedure Call (RPC)");
                    Logger.WarningNested("Trying again with OPENQUERY");
                    _linkedServers.UseRemoteProcedureCall = false;
                    return ExecuteWithHandling(query, executeReader, timeout, MAX_RETRIES - 1);
                }

                if (ex.Message.Contains("The metadata could not be determined"))
                {
                    Logger.Warning("DDL statement detected - wrapping query to make it OPENQUERY-compatible");
                    
                    // Wrap the query to return a result set - use EXEC to avoid metadata issues
                    string wrappedQuery = $"DECLARE @result NVARCHAR(MAX); BEGIN TRY {query.TrimEnd(';')}; SET @result = 'Success'; END TRY BEGIN CATCH SET @result = ERROR_MESSAGE(); END CATCH; SELECT @result AS Result;";
                    
                    Logger.WarningNested("Retrying with wrapped query");
                    return ExecuteWithHandling(wrappedQuery, executeReader, timeout, MAX_RETRIES - 1);
                }

                if (ex.Message.Contains("is not supported") && ex.Message.Contains("master.") && query.Contains("master."))
                {
                    Logger.Warning("Database prefix 'master.' not supported on remote server");
                    Logger.WarningNested("Retrying without database prefix");
                    
                    // Remove all master. prefixes from the query
                    string queryWithoutPrefix = query.Replace("master.", "");
                    
                    return ExecuteWithHandling(queryWithoutPrefix, executeReader, timeout, MAX_RETRIES - 1);
                }

                throw;
            }
        }

        /// <summary>
        /// Executes a SQL query against the database and returns a DataTable with the results.
        /// </summary>
        /// <param name="query">The SQL query to execute.</param>
        /// <returns>A DataTable containing the query results.</returns>
        public DataTable ExecuteTable(string query)
        {
            DataTable resultTable = new();

            // Ensure the reader is disposed.
            using SqlDataReader sqlDataReader = Execute(query);

            if (sqlDataReader is null)
            {
                Logger.Warning("No rows returned");   
                return resultTable;
            }

            resultTable.Load(sqlDataReader);

            return resultTable;
        }

        /// <summary>
        /// Executes a SQL query against the database and returns a single scalar value.
        /// </summary>
        /// <param name="query">The SQL query to execute.</param>
        /// <returns>The scalar value resulting from the query, or null if no rows are returned.</returns>
        public object ExecuteScalar(string query)
        {
            // Ensure the reader is disposed.
            using SqlDataReader sqlDataReader = Execute(query);

            if (sqlDataReader != null && sqlDataReader.Read()) // Check if there are rows and move to the first one.
            {
                return sqlDataReader.IsDBNull(0) ? null : sqlDataReader.GetValue(0); // Return the first column value in its original type.
            }

            return null;
        }

        /// <summary>
        /// Prepares the final query by adding linked server logic if needed.
        /// </summary>
        /// <param name="query">The initial SQL query.</param>
        /// <returns>The modified query, accounting for linked servers if applicable.</returns>
        private string PrepareQuery(string query)
        {
            query = RemoveSqlComments(query);
            Logger.Debug($"Query to execute: {query}");
            string finalQuery = query;

            if (!_linkedServers.IsEmpty)
            {
                Logger.DebugNested("Linked server detected");

                finalQuery = _linkedServers.UseRemoteProcedureCall
                    ? _linkedServers.BuildRemoteProcedureCallChain(query)
                    : _linkedServers.BuildSelectOpenQueryChain(query);


                Logger.DebugNested($"Linked query: {finalQuery}");
            }

            return finalQuery;
        }

        /// <summary>
        /// Removes SQL comments (single-line -- and multi-line /* */) from a query.
        /// </summary>
        /// <param name="query">The SQL query with potential comments.</param>
        /// <returns>The query without comments.</returns>
        private string RemoveSqlComments(string query)
        {
            // Remove multi-line comments /* ... */
            query = System.Text.RegularExpressions.Regex.Replace(query, @"/\*.*?\*/", "", System.Text.RegularExpressions.RegexOptions.Singleline);
            
            // Remove single-line comments -- ... (but preserve strings containing --)
            var lines = query.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                int commentIndex = -1;
                bool inString = false;
                char stringChar = '\0';
                
                for (int j = 0; j < lines[i].Length - 1; j++)
                {
                    char current = lines[i][j];
                    char next = lines[i][j + 1];
                    
                    // Track string boundaries
                    if ((current == '\'' || current == '"') && (j == 0 || lines[i][j - 1] != '\\'))
                    {
                        if (!inString)
                        {
                            inString = true;
                            stringChar = current;
                        }
                        else if (current == stringChar)
                        {
                            inString = false;
                        }
                    }
                    
                    // Find comment start (not inside a string)
                    if (!inString && current == '-' && next == '-')
                    {
                        commentIndex = j;
                        break;
                    }
                }
                
                if (commentIndex >= 0)
                {
                    lines[i] = lines[i].Substring(0, commentIndex).TrimEnd();
                }
            }
            
            return string.Join("\n", lines);
        }

        /// <summary>
        /// Checks if the current execution server is Azure SQL Database.
        /// Results are cached per server for performance.
        /// </summary>
        /// <returns>True if the server is Azure SQL, otherwise false.</returns>
        public bool IsAzureSQL()
        {
            // Check if Azure SQL detection is already cached for the current ExecutionServer
            if (_isAzureSQLCache.TryGetValue(ExecutionServer, out bool isAzure))
            {
                return isAzure;
            }

            // If not cached, detect and store the result
            bool azureStatus = DetectAzureSQL();

            // Cache the result for the current ExecutionServer
            _isAzureSQLCache[ExecutionServer] = azureStatus;

            if (azureStatus)
            {
                Logger.Debug($"Detected Azure SQL Database on {ExecutionServer}");
            }

            return azureStatus;
        }

        /// <summary>
        /// Detects if the current execution server is Azure SQL by checking @@VERSION.
        /// </summary>
        /// <returns>True if Azure SQL Database (PaaS) is detected, otherwise false.</returns>
        private bool DetectAzureSQL()
        {
            try
            {
                string version = ExecuteScalar("SELECT @@VERSION")?.ToString();
                
                if (string.IsNullOrEmpty(version))
                {
                    return false;
                }
                
                // Check if it contains "Microsoft SQL Azure"
                bool isAzure = version.Contains("Microsoft SQL Azure", StringComparison.OrdinalIgnoreCase);
                
                if (isAzure)
                {
                    // Distinguish between Azure SQL Database and Managed Instance
                    // Azure SQL Database (PaaS) contains "SQL Azure" but NOT "Managed Instance"
                    // Azure SQL Managed Instance contains both "SQL Azure" and specific MI indicators
                    bool isManagedInstance = version.Contains("Azure SQL Managed Instance", StringComparison.OrdinalIgnoreCase) ||
                                           version.Contains("SQL Azure Managed Instance", StringComparison.OrdinalIgnoreCase);
                    
                    if (isManagedInstance)
                    {
                        Logger.Info($"Detected Azure SQL Managed Instance on {ExecutionServer} - full feature set");
                        return false; // Managed Instance has full features
                    }
                    else
                    {
                        Logger.Info($"Detected Azure SQL Database (PaaS) on {ExecutionServer} - limited feature set");
                        return true; // PaaS has limitations
                    }
                }
                
                return false;
            }
            catch
            {
                // If detection fails, assume it's not Azure SQL
                return false;
            }
        }
    }
}
