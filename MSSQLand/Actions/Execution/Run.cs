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
    /// Execute a remote file on the SQL Server filesystem.
    /// 
    /// Runs executables, scripts, or batch files using OLE Automation (WScript.Shell)
    /// by default, or command shell as fallback.
    /// 
    /// Modes:
    /// - Default: async via OLE (fire and forget, non-blocking)
    /// - -w/--wait: sync via OLE (wait for completion, return exit code)
    /// - --xpcmd: sync via command shell (capture stdout)
    /// 
    /// Examples:
    ///   run C:\tool.exe                    (async via OLE)
    ///   run C:\tool.exe arg1 arg2          (async with args)
    ///   run C:\tool.exe -w                 (wait for exit code)
    ///   run C:\tool.exe --xpcmd            (capture output via command shell)
    /// </summary>
    internal class Run : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Remote file path to execute")]
        private string _filePath = "";

        [ArgumentMetadata(ShortName = "w", LongName = "wait", Description = "Execute synchronously (wait for completion)")]
        private bool _wait = false;

        [ArgumentMetadata(LongName = "xpcmd", Description = "Use command shell with output capture (forces sync)")]
        private bool _useXpCmd = false;

        [ArgumentMetadata(CaptureRemaining = true, Description = "Arguments to pass to the executable")]
        private string _arguments = "";

        /// <summary>
        /// Validates the arguments passed to the Run action.
        /// </summary>
        /// <param name="args">File path and optional flags/arguments.</param>
        public override void ValidateArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                throw new ArgumentException("Run action requires a file path as an argument.");
            }

            BindArguments(args);

            if (string.IsNullOrWhiteSpace(_filePath))
            {
                throw new ArgumentException("Run action requires a file path as an argument.");
            }

            // Normalize path
            _filePath = _filePath.Replace("/", "\\");

            // Log mode
            if (_useXpCmd)
            {
                Logger.Info("Command shell mode (synchronous with output capture)");
            }
            else if (_wait)
            {
                Logger.Info("Synchronous mode (will wait for completion)");
            }
            else
            {
                Logger.Info("Asynchronous mode (non-blocking)");
            }

            Logger.Info($"Target file: {_filePath}");
            if (!string.IsNullOrWhiteSpace(_arguments))
            {
                Logger.InfoNested($"Arguments: {_arguments}");
            }
        }

        /// <summary>
        /// Executes the remote file.
        /// </summary>
        /// <param name="databaseContext">The DatabaseContext instance to execute the query.</param>
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Executing remote file: {_filePath}");

            // Async mode = not waiting and not using command shell
            bool asyncMode = !_wait && !_useXpCmd;

            // If command shell is requested, force command shell
            if (_useXpCmd)
            {
                Logger.Info("Output capture requested, using command shell method");
                return ExecuteViaXpCmdshell(databaseContext, asyncMode);
            }

            // Check if OLE Automation is available
            bool oleAvailable = databaseContext.ConfigService.SetConfigurationOption("Ole Automation Procedures", 1);

            // Use OLE if available, otherwise command shell
            if (oleAvailable)
            {
                Logger.Info("OLE Automation is available, using OLE method");
                return ExecuteViaOle(databaseContext, asyncMode);
            }

            Logger.Info("OLE Automation not available, using command shell method");
            return ExecuteViaXpCmdshell(databaseContext, asyncMode);
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

                // Build the command string
                string command = string.IsNullOrWhiteSpace(_arguments) 
                    ? escapedPath 
                    : $"{escapedPath} {escapedArgs}";

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

        /// <summary>
        /// Execute the file using xp_cmdshell.
        /// </summary>
        /// <param name="databaseContext">The database context.</param>
        /// <param name="asyncMode">If true, use 'start' for async; if false, execute directly.</param>
        /// <returns>Success message (async) or output lines (sync).</returns>
        private object ExecuteViaXpCmdshell(DatabaseContext databaseContext, bool asyncMode)
        {
            // Enable xp_cmdshell if needed
            if (!databaseContext.ConfigService.SetConfigurationOption("xp_cmdshell", 1))
            {
                Logger.Error("Failed to enable xp_cmdshell");
                return null;
            }

            try
            {
                string command;
                string escapedCommand;
                string query;
                
                if (asyncMode)
                {
                    // Async execution with 'start' command
                    // /B = start without creating a new window
                    command = string.IsNullOrWhiteSpace(_arguments)
                        ? $"start /B \"\" \"{_filePath}\""
                        : $"start /B \"\" \"{_filePath}\" {_arguments}";

                    // Escape single quotes for SQL
                    escapedCommand = command.Replace("'", "''");

                    query = $"EXEC master..xp_cmdshell '{escapedCommand}'";

                    Logger.Info("Executing via xp_cmdshell (async)");
                    databaseContext.QueryService.ExecuteNonProcessing(query);
                    Logger.Success("File launched successfully via xp_cmdshell (running in background)");
                    return "Process launched in background";
                }

                // Sync execution - run directly and capture output
                command = string.IsNullOrWhiteSpace(_arguments)
                    ? $"\"{_filePath}\""
                    : $"\"{_filePath}\" {_arguments}";

                // Escape single quotes for SQL
                escapedCommand = command.Replace("'", "''");

                query = $"EXEC master..xp_cmdshell '{escapedCommand}'";

                Logger.Info("Executing via xp_cmdshell (sync)");
                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                List<string> outputLines = new List<string>();

                if (result != null && result.Rows.Count > 0)
                {
                    Logger.NewLine();
                    foreach (DataRow row in result.Rows)
                    {
                        // Handle NULL values - xp_cmdshell returns single column named "output"
                        string output = row[0] != DBNull.Value ? row[0].ToString() : "";
                        
                        Console.WriteLine(output);
                        outputLines.Add(output);
                    }

                    Logger.Success("File executed successfully via xp_cmdshell");
                    return outputLines;
                }

                Logger.Warning("The command executed but returned no results.");
                return outputLines;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("cannot find") || ex.Message.Contains("not found") || ex.Message.Contains("does not exist"))
                {
                    Logger.Error($"File does not exist: {_filePath}");
                }
                else
                {
                    Logger.Error($"Failed to execute via xp_cmdshell: {ex.Message}");
                }
                return null;
            }
        }
    }
}
