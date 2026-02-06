// MSSQLand/Actions/Execution/Run.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace MSSQLand.Actions.Execution
{
    /// <summary>
    /// Execute a remote file on the SQL Server filesystem using OLE Automation (WScript.Shell).
    ///
    /// Modes:
    /// - Default: async (fire and forget, non-blocking)
    /// - -w/--wait: sync (wait for completion, return exit code)
    ///
    /// Examples:
    ///   run C:\tool.exe                    (async)
    ///   run C:\tool.exe arg1 arg2          (async with args)
    ///   run C:\tool.exe -w                 (wait for exit code)
    ///
    /// Note: Requires OLE Automation. Use 'xpcmd' action if OLE is unavailable.
    /// </summary>
    internal class Run : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Remote file path to execute")]
        private string _filePath = "";

        [ArgumentMetadata(ShortName = "w", LongName = "wait", Description = "Execute synchronously (wait for completion)")]
        private bool _wait = false;

        [ArgumentMetadata(Position = 1, Description = "Arguments to pass to the executable")]
        private string _arguments = "";

        /// <summary>
        /// Validates the arguments and normalizes the file path.
        /// </summary>
        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);

            // Normalize path
            _filePath = _filePath.Replace("/", "\\");
        }

        /// <summary>
        /// Executes the remote file.
        /// </summary>
        /// <param name="databaseContext">The DatabaseContext instance to execute the query.</param>
        public override object Execute(DatabaseContext databaseContext)
        {
            // Log execution details
            if (_wait)
            {
                Logger.Info("Synchronous mode (will wait for completion)");
            }
            else
            {
                Logger.Info("Asynchronous mode (non-blocking)");
            }

            Logger.TaskNested($"Executing remote file: {_filePath}");
            if (!string.IsNullOrWhiteSpace(_arguments))
            {
                Logger.InfoNested($"Arguments: {_arguments}");
            }

            // Check if OLE Automation is available
            bool oleAvailable = databaseContext.ConfigService.SetConfigurationOption("Ole Automation Procedures", 1);

            if (!oleAvailable)
            {
                Logger.Error("Cannot enable OLE Automation (no ALTER SETTINGS permission)");
                Logger.ErrorNested("Use 'xpcmd' action if OLE Automation is unavailable");
                return null;
            }

            Logger.Info("OLE Automation is available, using OLE method");
            return ExecuteViaOle(databaseContext, !_wait);
        }

        /// <summary>
        /// Execute the file using OLE Automation with WScript.Shell.Run.
        /// </summary>
        /// <param name="databaseContext">The database context.</param>
        /// <param name="asyncMode">If true, don't wait for completion; if false, wait and return exit code.</param>
        /// <returns>Success message or exit code information.</returns>
        private object ExecuteViaOle(DatabaseContext databaseContext, bool asyncMode)
        {
            try
            {
                // Escape single quotes for SQL
                string escapedPath = _filePath.Replace("'", "''");
                string escapedArgs = _arguments.Replace("'", "''");

                // Build the command string (always quote path for spaces)
                string command = string.IsNullOrWhiteSpace(_arguments)
                    ? $"\"{escapedPath}\""
                    : $"\"{escapedPath}\" {escapedArgs}";

                // waitOnReturn: 0 = async (don't wait), 1 = sync (wait for completion)
                string waitParam = asyncMode ? "0" : "1";

                if (asyncMode)
                {
                    // Async mode - no exit code returned
                    // Creates WScript.Shell object and calls Run method with waitOnReturn=0 (don't wait)
                    string query = $@"
DECLARE @ObjectToken INT;
DECLARE @Result INT;
DECLARE @ErrorSource NVARCHAR(255);
DECLARE @ErrorDesc NVARCHAR(255);

EXEC @Result = sp_OACreate 'WScript.Shell', @ObjectToken OUTPUT;
IF @Result <> 0
BEGIN
    EXEC sp_OAGetErrorInfo @ObjectToken, @ErrorSource OUT, @ErrorDesc OUT;
    RAISERROR('Failed to create WScript.Shell: %s', 16, 1, @ErrorDesc);
    RETURN;
END

EXEC @Result = sp_OAMethod @ObjectToken, 'Run', NULL, '{command}', 0, {waitParam};
IF @Result <> 0
BEGIN
    EXEC sp_OAGetErrorInfo @ObjectToken, @ErrorSource OUT, @ErrorDesc OUT;
    EXEC sp_OADestroy @ObjectToken;
    RAISERROR('Failed to execute file: %s', 16, 1, @ErrorDesc);
    RETURN;
END

EXEC sp_OADestroy @ObjectToken;
";

                    databaseContext.QueryService.ExecuteNonProcessing(query);
                    Logger.Success("File launched successfully via OLE (running in background)");
                    return "Process launched in background";
                }

                // Sync mode - wait and return exit code
                // Creates WScript.Shell object and calls Run method with waitOnReturn=1 (wait for completion)
                // Returns the exit code from the executed process
                string syncQuery = $@"
DECLARE @ObjectToken INT;
DECLARE @Result INT;
DECLARE @ErrorSource NVARCHAR(255);
DECLARE @ErrorDesc NVARCHAR(255);
DECLARE @ExitCode INT;

EXEC @Result = sp_OACreate 'WScript.Shell', @ObjectToken OUTPUT;
IF @Result <> 0
BEGIN
    EXEC sp_OAGetErrorInfo @ObjectToken, @ErrorSource OUT, @ErrorDesc OUT;
    RAISERROR('Failed to create WScript.Shell: %s', 16, 1, @ErrorDesc);
    RETURN;
END

EXEC @Result = sp_OAMethod @ObjectToken, 'Run', @ExitCode OUTPUT, '{command}', 0, {waitParam};
IF @Result <> 0
BEGIN
    EXEC sp_OAGetErrorInfo @ObjectToken, @ErrorSource OUT, @ErrorDesc OUT;
    EXEC sp_OADestroy @ObjectToken;
    RAISERROR('Failed to execute file: %s', 16, 1, @ErrorDesc);
    RETURN;
END

EXEC sp_OADestroy @ObjectToken;

SELECT @ExitCode AS ExitCode;
";

                DataTable result = databaseContext.QueryService.ExecuteTable(syncQuery);

                if (result != null && result.Rows.Count > 0)
                {
                    int exitCode = result.Rows[0]["ExitCode"] != DBNull.Value
                        ? Convert.ToInt32(result.Rows[0]["ExitCode"])
                        : -1;
                    Logger.Success($"File executed successfully via OLE (Exit Code: {exitCode})");
                    return $"Exit code: {exitCode}";
                }

                Logger.Error("OLE execution failed");
                return null;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("cannot find") || ex.Message.Contains("not found") || ex.Message.Contains("does not exist"))
                {
                    Logger.Error($"File does not exist: {_filePath}");
                }
                else
                {
                    Logger.Error($"Failed to execute via OLE: {ex.Message}");
                }
                return null;
            }
        }


    }
}
