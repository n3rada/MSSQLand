﻿using MSSQLand.Models;
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
        /// <returns>Result of ExecuteReader if executeReader is true; otherwise null.</returns>
        private object ExecuteWithHandling(string query, bool executeReader)
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


            string finalQuery = PrepareQuery(query);

            try
            {

                using var command = new SqlCommand(finalQuery, Connection)
                {
                    CommandType = CommandType.Text,
                    CommandTimeout = 20
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
            catch (Exception ex)
            {
                Logger.Debug($"Query execution returned an error: {ex.Message}");

                if (ex.Message.Contains("not configured for RPC"))
                {
                    Logger.Warning("The targeted server is not configured for Remote Procedure Call (RPC)");
                    Logger.WarningNested("Trying again with OPENQUERY");
                    _linkedServers.UseRemoteProcedureCall = false;
                    return Execute(query);
                }

                if (ex.Message.Contains("The metadata could not be determined"))
                {
                    Logger.Error("When you wrap a remote procedure in OPENQUERY, SQL Server wants a single, consistent set of columns.");
                    Logger.ErrorNested("Since sp_configure does not provide that, the metadata parser chokes.");
                    Logger.Info("Enable RPC OUT option to allow the use of sp_configure.");
                    Logger.InfoNested($"/a:rpc add {ExecutionServer}");
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
