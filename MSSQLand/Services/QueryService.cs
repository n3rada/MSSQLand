using MSSQLand.Models;
using MSSQLand.Utilities;
using System;
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

            if (Connection == null || Connection.State != ConnectionState.Open)
            {
                Logger.Error("Database connection is not initialized or not open.");
                return null;
            }

            const int maxRetries = 3;
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
            catch (SqlException ex) when (ex.Message.Contains("Timeout") && retryCount < maxRetries)
            {
                int newTimeout = timeout * 2; // Exponential backoff
                Logger.Warning($"Query timed out after {timeout} seconds. Retrying with {newTimeout} seconds (attempt {retryCount + 1}/{maxRetries})");
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
                    return ExecuteWithHandling(query, executeReader, timeout, retryCount);
                }

                if (ex.Message.Contains("The metadata could not be determined"))
                {
                    Logger.Warning("DDL statement detected - wrapping query to make it OPENQUERY-compatible");
                    
                    // Wrap the query to return a result set
                    string wrappedQuery = $"BEGIN TRY {query.TrimEnd(';')} SELECT 'Success' AS Result END TRY BEGIN CATCH SELECT ERROR_MESSAGE() AS Result END CATCH";
                    
                    Logger.WarningNested("Retrying with wrapped query");
                    return ExecuteWithHandling(wrappedQuery, executeReader, timeout, retryCount);
                }

                if (ex.Message.Contains("is not supported") && ex.Message.Contains("master.") && query.Contains("master."))
                {
                    Logger.Warning("Database prefix 'master.' not supported on remote server");
                    Logger.WarningNested("Retrying without database prefix");
                    
                    // Remove all master. prefixes from the query
                    string queryWithoutPrefix = query.Replace("master.", "");
                    
                    return ExecuteWithHandling(queryWithoutPrefix, executeReader, timeout, retryCount);
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
    }
}
