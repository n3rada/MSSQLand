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
        public string ExecutionDatabase { get; set; }

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
                    
                    // Smart database detection
                    var lastServer = _linkedServers.ServerChain.Last();
                    if (!string.IsNullOrEmpty(lastServer.Database))
                    {
                        // Use explicitly specified database from chain
                        ExecutionDatabase = lastServer.Database;
                    }
                    else
                    {
                        // No explicit database: query to detect actual database
                        try
                        {
                            ExecutionDatabase = ExecuteScalar<string>("SELECT DB_NAME();");
                        }
                        catch
                        {
                            // If detection fails, keep default
                            ExecutionDatabase = "master";
                        }
                    }
                    
                    Logger.Debug($"Execution server set to: {ExecutionServer}");
                    Logger.Debug($"Execution database set to: {ExecutionDatabase}");
                }
                else
                {
                    ExecutionServer = GetServerName();
                    ExecutionDatabase = Connection.Database ?? "master";
                }
            }
        }

        public QueryService(SqlConnection connection)
        {
            Connection = connection;
            ExecutionServer = GetServerName();
            ExecutionDatabase = connection.Database ?? "master";
        }

        private string GetServerName()
        {
            // SELECT SERVERPROPERTY('MachineName')
            // SELECT @@SERVERNAME
            string serverName = ExecuteScalar<string>("SELECT @@SERVERNAME");

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
        /// <typeparam name="T">The type to convert the result to.</typeparam>
        /// <param name="query">The SQL query to execute.</param>
        /// <returns>The scalar value resulting from the query, or default(T) if no rows are returned.</returns>
        public T ExecuteScalar<T>(string query)
        {
            // Ensure the reader is disposed.
            using SqlDataReader sqlDataReader = Execute(query);

            if (sqlDataReader != null && sqlDataReader.Read()) // Check if there are rows and move to the first one.
            {
                object value = sqlDataReader.IsDBNull(0) ? null : sqlDataReader.GetValue(0);
                
                if (value == null)
                {
                    return default(T);
                }
                
                // Convert to the requested type
                return (T)Convert.ChangeType(value, typeof(T));
            }

            return default(T);
        }

        /// <summary>
        /// Prepares the final query by adding linked server logic if needed.
        /// </summary>
        /// <param name="query">The initial SQL query.</param>
        /// <returns>The modified query, accounting for linked servers if applicable.</returns>
        private string PrepareQuery(string query)
        {
            Logger.Debug($"Query to execute: {query}");
            string finalQuery = query;

            if (!_linkedServers.IsEmpty)
            {
                finalQuery = _linkedServers.UseRemoteProcedureCall
                    ? _linkedServers.BuildRemoteProcedureCallChain(query)
                    : _linkedServers.BuildSelectOpenQueryChain(query);


                Logger.DebugNested($"Linked query: {finalQuery}");
            }

            return finalQuery;
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
                string version = ExecuteScalar<string>("SELECT @@VERSION");
                
                if (string.IsNullOrEmpty(version))
                {
                    return false;
                }
                
                // Check if it contains "Microsoft SQL Azure" (case-insensitive)
                bool isAzure = version.IndexOf("Microsoft SQL Azure", StringComparison.OrdinalIgnoreCase) >= 0;
                
                if (isAzure)
                {
                    // Distinguish between Azure SQL Database and Managed Instance
                    // Azure SQL Database (PaaS) contains "SQL Azure" but NOT "Managed Instance"
                    // Azure SQL Managed Instance contains both "SQL Azure" and specific MI indicators
                    bool isManagedInstance = version.IndexOf("Azure SQL Managed Instance", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                           version.IndexOf("SQL Azure Managed Instance", StringComparison.OrdinalIgnoreCase) >= 0;
                    
                    if (isManagedInstance)
                    {
                        Logger.Info($"Detected Azure SQL Managed Instance on {ExecutionServer}");
                        return false; // Managed Instance has full features
                    }
                    else
                    {
                        Logger.Info($"Detected Azure SQL Database (PaaS) on {ExecutionServer}");
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
