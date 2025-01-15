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
        private LinkedServers _linkedServers;

        /// <summary>
        /// LinkedServers property. When set, updates the ExecutionServer to the last server in the ServerNames array.
        /// </summary>
        public LinkedServers LinkedServers
        {
            get => _linkedServers;
            set
            {
                _linkedServers = value;
                if (_linkedServers?.ServerNames != null && _linkedServers.ServerNames.Length > 0)
                {
                    ExecutionServer = _linkedServers.ServerNames.Last();
                }
                else
                {
                    ExecutionServer = null;
                }
            }
        }

        public QueryService(SqlConnection connection)
        {
            Connection = connection;
            ExecutionServer = connection.WorkstationId;
        }


        /// <summary>
        /// Executes a SQL query against the database and returns a single scalar value.
        /// </summary>
        /// <param name="query">The SQL query to execute.</param>
        /// <returns>The SqlDataReader resulting from the query.</returns>
        public SqlDataReader Execute(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                throw new ArgumentException("Query cannot be null or empty.", nameof(query));
            }

            string finalQuery = PrepareQuery(query);

            try
            {

                using var command = new SqlCommand(finalQuery, Connection)
                {
                    CommandType = CommandType.Text
                };

                return command.ExecuteReader();
            }
            catch (Exception ex)
            {
                Logger.Warning($"Query execution failed: {ex.Message}");

                if (ex.Message.Contains("not configured for RPC"))
                {
                    Logger.WarningNested("Trying again with OPENQUERY");
                    _linkedServers.SupportRemoteProcedureCall = false;
                    return Execute(query);
                }
                return null;
            }
        }

        public void ExecuteNonProcessing(string query)
        {
            using var reader = Execute(query);
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
            string finalQuery = query;

            Logger.Debug($"Executing on: {ExecutionServer}");
            Logger.DebugNested($"Initial Query: {query}");

            // If LinkedServers variable exists and has valid server names
            if (_linkedServers?.ServerNames != null && _linkedServers.ServerNames.Length > 0)
            {
                if (_linkedServers.SupportRemoteProcedureCall)
                {
                    finalQuery = _linkedServers.BuildRemoteProcedureCallChain(query);
                }
                else
                {
                    finalQuery = _linkedServers.BuildOpenQueryChain(query);
                }

                Logger.DebugNested($"Linked Query: {finalQuery}");
            }

            return finalQuery;
        }
    }
}
