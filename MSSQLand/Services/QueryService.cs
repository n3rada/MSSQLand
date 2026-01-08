// MSSQLand/Services/QueryService.cs

using MSSQLand.Models;
using MSSQLand.Utilities;
using MSSQLand.Exceptions;
using System;
using System.Collections.Concurrent;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace MSSQLand.Services
{
    /// <summary>
    /// Central query execution engine with automatic handling for:
    ///
    /// - RPC vs OPENQUERY routing
    /// - Non-rowset OPENQUERY wrapping
    /// - Timeout retries with exponential backoff
    /// - Linked server chaining
    /// - Execution context tracking
    /// - Azure SQL detection
    ///
    /// Responsibilities:
    /// - Decide whether a statement must execute using RPC
    /// - Downgrade to OPENQUERY when RPC is unavailable
    /// - Refuse server-scoped commands when only OPENQUERY is available
    /// - Convert non-rowset statements into OPENQUERY-safe result queries
    ///
    /// Execution Model:
    /// - If no linked servers are configured: execute query locally.
    /// - If linked servers exist:
    ///     - Prefer EXEC AT (RPC) when available.
    ///     - Fallback to OPENQUERY if RPC is disabled or unavailable.
    ///     - Reject server-level commands when only OPENQUERY is available.
    /// </summary>
    public class QueryService
    {
        /// <summary>
        /// Underlying SQL Server connection used to execute all statements.
        /// </summary>
        public readonly SqlConnection Connection;

        /// <summary>
        /// The server where queries actually execute (may differ from connection server when using linked servers).
        /// </summary>
        public Server ExecutionServer { get; set; }

        private LinkedServers _linkedServers = new();
        private const int MAX_RETRIES = 3;

        /// <summary>
        /// Per-server Azure SQL detection cache.
        /// </summary>
        private readonly ConcurrentDictionary<string, bool> _isAzureSQLCache = new();

        /// <summary>
        /// Determines if a SQL statement requires RPC execution because it modifies server-level state.
        /// These commands cannot be executed over OPENQUERY.
        /// </summary>
        /// <param name="sql">Input SQL query.</param>
        /// <returns>
        /// True if execution requires RPC; otherwise false.
        /// </returns>
        static bool RequiresRPC(string sql)
        {
            string s = sql.ToUpperInvariant();

            return s.Contains("CREATE LOGIN") ||
                   s.Contains("ALTER LOGIN") ||
                   s.Contains("DROP LOGIN") ||
                   s.Contains("ALTER SERVER") ||
                   s.Contains("SP_CONFIGURE") ||
                   s.Contains("RECONFIGURE") ||
                   s.Contains("XP_") ||
                   s.Contains("CREATE ENDPOINT");
        }

        /// <summary>
        /// Detects failure cases where OPENQUERY rejects a query because no rowset is returned.
        /// </summary>
        /// <param name="ex">Thrown exception.</param>
        /// <returns>
        /// True if the failure matches typical OPENQUERY rowset errors.
        /// </returns>
        static bool IsOpenQueryRowsetFailure(Exception ex)
        {
            string m = ex.Message;

            return m.Contains("metadata") ||
                   m.Contains("no columns") ||
                   m.Contains("Deferred prepare");
        }

        /// <summary>
        /// Wraps a non-rowset SQL statement into an OPENQUERY-compatible query by forcing
        /// a resultset output.
        /// </summary>
        /// <param name="query">Raw SQL statement.</param>
        /// <returns>Query wrapped as a SELECTable result.</returns>
        static string WrapForOpenQuery(string query)
        {
            return $@"
DECLARE @result NVARCHAR(MAX);
DECLARE @error NVARCHAR(MAX);
BEGIN TRY
    {query.TrimEnd(';')};
    SET @result = CAST(@@ROWCOUNT AS NVARCHAR(MAX));
    SET @error = NULL;
END TRY
BEGIN CATCH
    SET @result = NULL;
    SET @error = ERROR_MESSAGE();
END CATCH;
SELECT @result AS Result, @error AS Error;";
        }

        /// <summary>
        /// Linked server chain configuration.
        /// Automatically updates execution target when modified.
        /// </summary>
        public LinkedServers LinkedServers
        {
            get => _linkedServers;
            set
            {
                _linkedServers = value ?? new LinkedServers();
                ComputeExecutionServer();
            }
        }

        /// <summary>
        /// Initializes a QueryService bound to an existing SQL connection.
        /// </summary>
        /// <param name="connection">Active SQL connection.</param>
        public QueryService(SqlConnection connection)
        {
            Connection = connection;
            // ExecutionServer will be set by DatabaseContext with authenticated Server
            // or by ComputeExecutionServer() when linked servers are configured
        }

        /// <summary>
        /// Executes a query and returns a reader.
        /// </summary>
        public SqlDataReader Execute(string query)
            => ExecuteWithHandling(query, executeReader: true) as SqlDataReader;

        /// <summary>
        /// Executes a non-query statement and returns affected rows.
        /// </summary>
        public int ExecuteNonProcessing(string query)
        {
            var result = ExecuteWithHandling(query, executeReader: false);
            return result == null ? -1 : (int)result;
        }

        /// <summary>
        /// Executes a query with retry, fallback, and wrapping logic.
        /// Central execution pipeline.
        /// </summary>
        private object ExecuteWithHandling(string query, bool executeReader, int timeout = 120, int retryCount = 0)
        {
            if (string.IsNullOrEmpty(query))
                throw new ArgumentException("Query cannot be null or empty.", nameof(query));

            if (retryCount > MAX_RETRIES)
            {
                Logger.Error("Maximum retry attempts exceeded.");
                return null;
            }

            if (Connection.State != ConnectionState.Open)
                return null;

            string finalQuery = PrepareQuery(query);

            try
            {
                using var command = new SqlCommand(finalQuery, Connection)
                {
                    CommandType = CommandType.Text,
                    CommandTimeout = timeout
                };

                return executeReader
                    ? command.ExecuteReader()
                    : command.ExecuteNonQuery();
            }
            catch (SqlException ex) when (ex.Message.Contains("Timeout"))
            {
                Logger.Warning($"Timeout after {timeout}s. Retrying.");
                return ExecuteWithHandling(query, executeReader, timeout * 2, retryCount + 1);
            }
            catch (Exception ex)
            {
                Logger.Debug($"Execution error:\n{ex.Message}");

                if (ex.Message.Contains("not configured for RPC"))
                {
                    Logger.Warning("RPC unavailable. Switching to OPENQUERY.");
                    _linkedServers.UseRemoteProcedureCall = false;
                    return ExecuteWithHandling(query, executeReader, timeout, retryCount + 1);
                }

                if (!_linkedServers.UseRemoteProcedureCall && IsOpenQueryRowsetFailure(ex))
                {
                    Logger.Debug("OPENQUERY returned no rowset. Wrapping query.");
                    return ExecuteWithHandling(WrapForOpenQuery(query), executeReader, timeout, retryCount + 1);
                }

                throw;
            }
        }

        /// <summary>
        /// Builds the final executable SQL statement based on
        /// the linked server configuration.
        /// </summary>
        private string PrepareQuery(string query)
        {
            Logger.Debug($"Query to execute: {query}");

            if (_linkedServers.IsEmpty)
                return query;

            if (!_linkedServers.UseRemoteProcedureCall && RequiresRPC(query))
            {
                Logger.Warning("Server-level command rejected under OPENQUERY.");
                throw new RpcRequiredException(query);
            }

            string finalQuery = _linkedServers.UseRemoteProcedureCall
                ? _linkedServers.BuildRemoteProcedureCallChain(query)
                : _linkedServers.BuildSelectOpenQueryChain(query);

            Logger.DebugNested($"Linked query: {finalQuery}");
            return finalQuery;
        }

        /// <summary>
        /// Executes a query and loads result into a DataTable.
        /// </summary>
        public DataTable ExecuteTable(string query)
        {
            DataTable dt = new();
            using SqlDataReader rdr = Execute(query);
            dt.Load(rdr);
            return dt;
        }

        /// <summary>
        /// Executes a query and returns the first column of the first row.
        /// </summary>
        public object ExecuteScalar(string query)
        {
            using SqlDataReader reader = Execute(query);
            return reader != null && reader.Read()
                ? reader.IsDBNull(0) ? null : reader.GetValue(0)
                : null;
        }

        /// <summary>
        /// Determines whether the final execution server is Azure SQL Database (PaaS).
        /// Results are cached.
        /// </summary>
        public bool IsAzureSQL()
        {
            if (_isAzureSQLCache.TryGetValue(ExecutionServer.Hostname, out bool cached))
                return cached;

            bool isAzure = DetectAzureSQL();
            _isAzureSQLCache[ExecutionServer.Hostname] = isAzure;
            return isAzure;
        }

        /// <summary>
        /// Detects Azure SQL by inspecting @@VERSION output.
        /// </summary>
        private bool DetectAzureSQL()
        {
            try
            {
                string version = ExecuteScalar("SELECT @@VERSION")?.ToString();
                if (string.IsNullOrEmpty(version)) return false;

                bool azure = version.IndexOf("Microsoft SQL Azure", StringComparison.OrdinalIgnoreCase) >= 0;
                bool mi = version.IndexOf("Managed Instance", StringComparison.OrdinalIgnoreCase) >= 0;

                if (azure && !mi)
                    Logger.Info($"Azure SQL Database detected on {ExecutionServer.Hostname}");

                return azure && !mi;
            }
            catch { return false; }
        }

        /// <summary>
        /// Resolves the execution server context (hostname, version, database) for linked servers.
        /// For direct connections, ExecutionServer is already set with version from authentication.
        /// </summary>
        public void ComputeExecutionServer()
        {
            // For linked servers, use the last server in the chain as base
            if (_linkedServers.IsEmpty){
                // Direct connection: ExecutionServer is already set
                return;
            }
            
            Server last = _linkedServers.ServerChain.Last();
            ExecutionServer = last;

            // Query the actual server name from the last linked server
            try
            {
                ExecutionServer.Hostname = ExecuteScalar("SELECT @@SERVERNAME")?.ToString() ?? string.Empty;
            }
            catch
            {
                // Keep the configured hostname if query fails
            }

            // Query and set the server version for linked server
            try
            {
                string version = ExecuteScalar("SELECT @@VERSION")?.ToString();
                if (!string.IsNullOrEmpty(version))
                {
                    // Extract version number from version string
                    // Example: "Microsoft SQL Server 2019 (RTM) - 15.0.2000.5..."
                    var match = System.Text.RegularExpressions.Regex.Match(version, @"\s(\d+\.\d+\.\d+)");
                    if (match.Success)
                    {
                        ExecutionServer.Version = match.Groups[1].Value;
                        Logger.Debug($"Execution server version: {ExecutionServer.Version} (Major: {ExecutionServer.MajorVersion})");
                    }
                }
            }
            catch
            {
                // Version detection is optional
            }

            // Set database: use configured database or query DB_NAME()
            if (string.IsNullOrEmpty(last.Database))
            {
                try
                {
                    ExecutionServer.Database = ExecuteScalar("SELECT DB_NAME();")?.ToString();
                    Logger.Debug($"Detected execution database: {ExecutionServer.Database}");
                }
                catch
                {
                    ExecutionServer.Database = null;
                }
            }
        }
    }
}
