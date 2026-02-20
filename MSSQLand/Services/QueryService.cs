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
        private static bool _rpcWarningShown = false;

        /// <summary>
        /// Tracks linked servers already reported as unreachable to suppress duplicate error messages.
        /// </summary>
        private readonly System.Collections.Generic.HashSet<string> _reportedUnreachableServers = new(StringComparer.OrdinalIgnoreCase);

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
        /// Detects if an exception is a terminal impersonation failure.
        /// These are definitive errors — no retry, wrapping, or routing change will resolve them.
        /// SQL Server may append a secondary "metadata could not be determined" message
        /// which would otherwise trigger OPENQUERY wrapping; this check prevents that.
        /// </summary>
        static bool IsImpersonationFailure(Exception ex)
        {
            string m = ex.Message;

            return m.Contains("Cannot execute as the server principal") ||
                   m.Contains("cannot be impersonated");
        }

        /// <summary>
        /// Extracts the server name from a "not configured for RPC" error message.
        /// Example: "Server 'LAB-SQL01\SQL01' is not configured for RPC." → "LAB-SQL01\SQL01"
        /// </summary>
        /// <param name="errorMessage">The SQL error message.</param>
        /// <returns>The server name, or null if not found.</returns>
        static string ExtractNonRpcServer(string errorMessage)
        {
            const string prefix = "Server '";
            const string suffix = "' is not configured for RPC";

            int startIdx = errorMessage.IndexOf(prefix);
            if (startIdx < 0) return null;
            startIdx += prefix.Length;

            int endIdx = errorMessage.IndexOf(suffix, startIdx);
            if (endIdx < 0) return null;

            return errorMessage.Substring(startIdx, endIdx - startIdx);
        }

        /// <summary>
        /// Detects if a query has already been wrapped by WrapForOpenQuery.
        /// </summary>
        static bool IsAlreadyWrapped(string sql)
        {
            return sql.Contains("DECLARE @result NVARCHAR(MAX)") ||
                   sql.Contains("SELECT @result AS Result, @error AS Error");
        }

        /// <summary>
        /// Detects if an exception represents a connection or timeout failure to a linked server.
        /// </summary>
        static bool IsLinkedServerConnectionFailure(Exception ex)
        {
            string m = ex.Message;

            // Check for OLE DB provider errors (linked server specific)
            if (m.Contains("OLE DB provider") && m.Contains("for linked server"))
                return true;

            // Check for connection error patterns and error numbers in message
            return m.Contains("Login timeout expired") ||
                   m.Contains("Could not open a connection") ||
                   m.Contains("Named Pipes Provider") ||
                   m.Contains("TCP Provider") ||
                   m.Contains("no login-mapping") ||
                   m.Contains("[53]") ||    // SQL Server not found
                   m.Contains("[17]") ||    // SQL Server does not exist or access denied
                   m.Contains("[2]") ||     // Network timeout
                   m.Contains("[40]");      // Cannot open connection
        }

        /// <summary>
        /// Detects if a query is primarily a data-returning SELECT that shouldn't be wrapped.
        /// These queries return actual result sets and wrapping them causes metadata conflicts.
        /// </summary>
        static bool IsDataReturningSelect(string sql)
        {
            string normalized = sql.Trim().ToUpperInvariant();

            // Skip USE statements to find the actual query
            int useEnd = 0;
            while (true)
            {
                int useIdx = normalized.IndexOf("USE [", useEnd);
                if (useIdx == -1 || useIdx > useEnd + 50) break; // No USE or too far
                int bracket = normalized.IndexOf("]", useIdx);
                if (bracket == -1) break;
                int semi = normalized.IndexOf(";", bracket);
                useEnd = semi > bracket ? semi + 1 : bracket + 1;
            }

            string afterUse = normalized.Substring(useEnd).TrimStart();

            // If it starts with SELECT and has FROM, it's likely returning data
            // Exclude simple scalar selects like SELECT @@VERSION, SELECT DB_NAME()
            if (afterUse.StartsWith("SELECT") &&
                afterUse.Contains(" FROM ") &&
                !afterUse.Contains("INSERT") &&
                !afterUse.Contains("UPDATE") &&
                !afterUse.Contains("DELETE"))
            {
                return true;
            }

            return false;
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
            catch (SqlException ex) when (ex.Message.Contains("Timeout") && !IsLinkedServerConnectionFailure(ex))
            {
                Logger.Warning($"Query timeout after {timeout}s. Retrying with extended timeout.");
                return ExecuteWithHandling(query, executeReader, timeout * 2, retryCount + 1);
            }
            catch (Exception ex)
            {
                Logger.Debug($"Execution error:\n{ex.Message}");

                // Handle linked server connection failures
                if (IsLinkedServerConnectionFailure(ex))
                {
                    // We know which server failed from ExecutionServer
                    string failedServer = !_linkedServers.IsEmpty
                        ? ExecutionServer.LinkedServerAlias ?? ExecutionServer.Hostname
                        : ExecutionServer.Hostname;

                    // Only log the error the first time for each server
                    if (_reportedUnreachableServers.Add(failedServer))
                    {
                        Logger.Error($"Cannot reach linked server '{failedServer}'.");
                    }

                    throw; // Don't retry connection failures
                }

                if (ex.Message.Contains("not configured for RPC"))
                {
                    string failedServer = ExtractNonRpcServer(ex.Message);

                    if (failedServer != null)
                    {
                        _linkedServers.MarkServerAsNonRpc(failedServer);

                        if (_linkedServers.AllServersNonRpc)
                        {
                            // Every server in the chain lacks RPC — full OPENQUERY fallback
                            if (!_rpcWarningShown)
                            {
                                Logger.Debug("All linked servers lack RPC. Switching to full OPENQUERY.");
                                _rpcWarningShown = true;
                            }
                            _linkedServers.UseRemoteProcedureCall = false;
                        }
                        else
                        {
                            // Only some hops lack RPC — use hybrid routing
                            Logger.Debug($"RPC unavailable for '{failedServer}'. Using hybrid RPC/OPENQUERY routing.");
                        }
                    }
                    else
                    {
                        // Could not extract server name — fall back to full OPENQUERY
                        if (!_rpcWarningShown)
                        {
                            Logger.Debug("RPC unavailable. Switching to OPENQUERY.");
                            _rpcWarningShown = true;
                        }
                        _linkedServers.UseRemoteProcedureCall = false;
                    }

                    return ExecuteWithHandling(query, executeReader, timeout, retryCount + 1);
                }

                // Impersonation failures are terminal — wrapping or retrying won't help.
                // Check this before IsOpenQueryRowsetFailure because SQL Server appends
                // "The metadata could not be determined..." to impersonation errors,
                // which would otherwise trigger the OPENQUERY wrapping path.
                if (IsImpersonationFailure(ex))
                {
                    throw;
                }

                if ((!_linkedServers.UseRemoteProcedureCall || _linkedServers.HasNonRpcServers) && IsOpenQueryRowsetFailure(ex))
                {
                    // Don't wrap if already wrapped (prevents infinite loop)
                    if (IsAlreadyWrapped(query))
                    {
                        Logger.Debug("Query already wrapped, cannot recover from OPENQUERY failure.");
                        throw;
                    }

                    // Don't wrap data-returning SELECTs - they have a different issue
                    // (usually USE statement incompatibility with OPENQUERY)
                    if (IsDataReturningSelect(query))
                    {
                        Logger.Debug("Data-returning SELECT failed via OPENQUERY. USE statements may be incompatible.");
                        Logger.Warning("Query contains USE statement which is incompatible with OPENQUERY. Consider using 3-part names (database.schema.table) instead.");
                        throw;
                    }

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
            Logger.Debug($"Query to execute:\n{query}");

            if (_linkedServers.IsEmpty)
                return query;

            if (!_linkedServers.UseRemoteProcedureCall && RequiresRPC(query))
            {
                Logger.Warning("Server-level command rejected under OPENQUERY.");
                throw new RpcRequiredException(query);
            }

            string finalQuery;

            if (_linkedServers.UseRemoteProcedureCall && _linkedServers.HasNonRpcServers)
            {
                // Hybrid mode: RPC for capable hops, OPENQUERY for non-RPC hops
                finalQuery = _linkedServers.BuildHybridChain(query);
            }
            else if (_linkedServers.UseRemoteProcedureCall)
            {
                finalQuery = _linkedServers.BuildRemoteProcedureCallChain(query);
            }
            else
            {
                finalQuery = _linkedServers.BuildSelectOpenQueryChain(query);
            }

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
        /// Returns whether the execution server is Azure SQL Database (PaaS).
        /// Detection happens automatically when FullVersionString is set.
        /// </summary>
        public bool IsAzureSQL()
        {
            return ExecutionServer.IsAzureSQL;
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

            // IMPORTANT: Create a COPY to avoid mutating the actual chain entry
            // The chain must preserve the original alias for query routing
            ExecutionServer = last.Copy();

            // Store the linked server alias (from sys.servers) for query routing
            // This is the name we use in EXEC AT / OPENQUERY
            string linkedServerAlias = last.Hostname;
            ExecutionServer.LinkedServerAlias = linkedServerAlias;

            // Query server name and version in one shot
            // Setting FullVersionString automatically detects Azure SQL
            try
            {
                DataTable result = ExecuteTable("SELECT @@SERVERNAME, @@VERSION");
                if (result.Rows.Count > 0)
                {
                    DataRow row = result.Rows[0];

                    string actualServerName = row[0]?.ToString();
                    if (!string.IsNullOrEmpty(actualServerName))
                    {
                        ExecutionServer.Hostname = actualServerName;
                        if (!actualServerName.Equals(linkedServerAlias, StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.Trace($"Linked server alias '{linkedServerAlias}' resolves to '{actualServerName}'");
                        }
                    }

                    // Setting FullVersionString triggers version extraction and Azure SQL detection
                    ExecutionServer.FullVersionString = row[1].ToString();
                    Logger.Debug($"Execution server version: {ExecutionServer.Version} (Major: {ExecutionServer.MajorVersion})");
                }
            }
            catch
            {
                // Server info detection is optional - keep alias as hostname
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
