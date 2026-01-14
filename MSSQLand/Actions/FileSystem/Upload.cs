// MSSQLand/Actions/FileSystem/Upload.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;
using System.IO;
using System.Text;

namespace MSSQLand.Actions.FileSystem
{
    /// <summary>
    /// Upload a local file to the SQL Server filesystem.
    /// 
    /// This action reads a file from the local filesystem and writes it to a
    /// remote path on the SQL Server using OLE Automation (ADODB.Stream) or
    /// xp_cmdshell with PowerShell. After upload, it verifies the file was created.
    /// 
    /// Methods used (in order of preference):
    /// 1. OLE Automation with ADODB.Stream (most compatible, handles binary data well)
    /// 2. xp_cmdshell with PowerShell -EncodedCommand (if OLE is disabled, chunked for large files)
    /// </summary>
    internal class Upload : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Local file path to upload")]
        private string _localPath = "";

        [ArgumentMetadata(Position = 1, Description = "Remote destination path (defaults to C:\\Windows\\Tasks\\)")]
        private string _remotePath = "";

        private FileInfo _localFileInfo;

        /// <summary>
        /// Validates the arguments passed to the Upload action.
        /// </summary>
        /// <param name="args">Local file path and optional remote destination path.</param>
        public override void ValidateArguments(string[] args)
        {
            var (namedArgs, positionalArgs) = ParseActionArguments(args);

            // Get local path from positional argument
            _localPath = GetPositionalArgument(positionalArgs, 0, null);
            if (string.IsNullOrWhiteSpace(_localPath))
            {
                throw new ArgumentException("Upload action requires a local file path as the first argument.");
            }

            // Expand environment variables and resolve to absolute path
            _localPath = Environment.ExpandEnvironmentVariables(_localPath);

            // Validate local file exists
            _localFileInfo = new FileInfo(_localPath);
            if (!_localFileInfo.Exists)
            {
                throw new ArgumentException($"Local file does not exist: {_localPath}");
            }

            if (_localFileInfo.Length == 0)
            {
                throw new ArgumentException($"Local file is empty: {_localPath}");
            }

            // Get remote path from positional argument or use default
            _remotePath = GetPositionalArgument(positionalArgs, 1, null);

            if (string.IsNullOrWhiteSpace(_remotePath))
            {
                // Use C:\Windows\Tasks\ as default (world-writable directory)
                _remotePath = $"C:\\Windows\\Tasks\\{_localFileInfo.Name}";
                Logger.Info($"No remote path specified, using default: {_remotePath}");
            }
            else
            {
                // Normalize path
                _remotePath = _remotePath.Replace("/", "\\");
                
                // If remote path ends with backslash, it's a directory - append filename
                if (_remotePath.EndsWith("\\"))
                {
                    _remotePath = _remotePath + _localFileInfo.Name;
                    Logger.Info("Remote path is a directory, appending filename");
                }
            }

            Logger.Info($"Local file: {_localFileInfo.FullName}");
            Logger.Info($"File size: {_localFileInfo.Length:N0} bytes");
            Logger.Info($"Remote destination: {_remotePath}");
        }

        /// <summary>
        /// Executes the upload action.
        /// </summary>
        /// <param name="databaseContext">The DatabaseContext instance to execute the query.</param>
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Uploading file to SQL Server");

            // Read local file content
            byte[] fileContent;
            try
            {
                fileContent = File.ReadAllBytes(_localFileInfo.FullName);
                Logger.Info($"Read {fileContent.Length:N0} bytes from local file");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to read local file: {ex.Message}");
                return false;
            }

            // Check if OLE Automation is available
            bool oleAvailable = databaseContext.ConfigService.SetConfigurationOption("Ole Automation Procedures", 1);

            bool success;
            if (oleAvailable)
            {
                Logger.Info("OLE Automation is available, using OLE method");
                success = UploadViaOle(databaseContext, fileContent);
            }
            else
            {
                Logger.Info("OLE Automation not available, using PowerShell method");
                success = UploadViaXpCmdshell(databaseContext, fileContent);
            }

            if (!success)
            {
                Logger.Error("Upload failed");
                return false;
            }

            return VerifyUpload(databaseContext);
        }

        /// <summary>
        /// Verifies that the file was uploaded successfully using xp_fileexist.
        /// </summary>
        /// <param name="databaseContext">The database context.</param>
        /// <returns>True if file exists; otherwise false.</returns>
        private bool VerifyUpload(DatabaseContext databaseContext)
        {
            try
            {
                string escapedPath = _remotePath.Replace("'", "''");

                // Use xp_fileexist to check if file exists
                string query = $"EXEC master..xp_fileexist '{escapedPath}'";
                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result == null || result.Rows.Count == 0)
                {
                    Logger.Error($"Could not verify file existence at: {_remotePath}");
                    return false;
                }

                // xp_fileexist returns: File Exists, File is a Directory, Parent Directory Exists
                bool fileExists = result.Rows[0][0] != DBNull.Value && Convert.ToInt32(result.Rows[0][0]) == 1;

                if (!fileExists)
                {
                    Logger.Error($"File was not created at: {_remotePath}");
                    return false;
                }

                Logger.Success($"File uploaded successfully to: {_remotePath}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Could not verify upload: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Upload file using OLE Automation with ADODB.Stream.
        /// </summary>
        /// <param name="databaseContext">The database context.</param>
        /// <param name="fileContent">The file content as bytes.</param>
        /// <returns>True if upload succeeded; otherwise false.</returns>
        private bool UploadViaOle(DatabaseContext databaseContext, byte[] fileContent)
        {
            Logger.Info("Uploading file via OLE Automation (ADODB.Stream)");

            // For large files, this might fail due to VARBINARY(MAX) limits
            if (fileContent.Length > 2000000000) // ~2GB limit
            {
                Logger.Warning("File too large for OLE method (>2GB), will try xp_cmdshell");
                return false;
            }

            // Convert bytes to hex string for SQL
            string hexContent = Misc.BytesToHexString(fileContent);

            // Escape single quotes in remote path
            string escapedRemotePath = _remotePath.Replace("'", "''");

            // Use ADODB.Stream to write binary data
            // Creates ADODB.Stream object, sets Type=1 (binary), writes hex content, and saves to file
            // Mode 2 in SaveToFile means overwrite if file exists
            string query = $@"
DECLARE @ObjectToken INT;
DECLARE @FileContent VARBINARY(MAX);
DECLARE @Result INT;
DECLARE @ErrorSource NVARCHAR(255);
DECLARE @ErrorDesc NVARCHAR(255);

SET @FileContent = 0x{hexContent};

EXEC @Result = sp_OACreate 'ADODB.Stream', @ObjectToken OUTPUT;
IF @Result <> 0
BEGIN
    EXEC sp_OAGetErrorInfo @ObjectToken, @ErrorSource OUT, @ErrorDesc OUT;
    RAISERROR('Failed to create ADODB.Stream: %s', 16, 1, @ErrorDesc);
    RETURN;
END

EXEC @Result = sp_OASetProperty @ObjectToken, 'Type', 1;
IF @Result <> 0
BEGIN
    EXEC sp_OAGetErrorInfo @ObjectToken, @ErrorSource OUT, @ErrorDesc OUT;
    EXEC sp_OADestroy @ObjectToken;
    RAISERROR('Failed to set stream type: %s', 16, 1, @ErrorDesc);
    RETURN;
END

EXEC @Result = sp_OAMethod @ObjectToken, 'Open';
IF @Result <> 0
BEGIN
    EXEC sp_OAGetErrorInfo @ObjectToken, @ErrorSource OUT, @ErrorDesc OUT;
    EXEC sp_OADestroy @ObjectToken;
    RAISERROR('Failed to open stream: %s', 16, 1, @ErrorDesc);
    RETURN;
END

EXEC @Result = sp_OAMethod @ObjectToken, 'Write', NULL, @FileContent;
IF @Result <> 0
BEGIN
    EXEC sp_OAGetErrorInfo @ObjectToken, @ErrorSource OUT, @ErrorDesc OUT;
    EXEC sp_OAMethod @ObjectToken, 'Close';
    EXEC sp_OADestroy @ObjectToken;
    RAISERROR('Failed to write to stream: %s', 16, 1, @ErrorDesc);
    RETURN;
END

EXEC @Result = sp_OAMethod @ObjectToken, 'SaveToFile', NULL, '{escapedRemotePath}', 2;
IF @Result <> 0
BEGIN
    EXEC sp_OAGetErrorInfo @ObjectToken, @ErrorSource OUT, @ErrorDesc OUT;
    EXEC sp_OAMethod @ObjectToken, 'Close';
    EXEC sp_OADestroy @ObjectToken;
    RAISERROR('Failed to save file to {escapedRemotePath}: %s', 16, 1, @ErrorDesc);
    RETURN;
END

EXEC sp_OAMethod @ObjectToken, 'Close';
EXEC sp_OADestroy @ObjectToken;
";

            try
            {
                databaseContext.QueryService.ExecuteNonProcessing(query);
                Logger.Info("OLE upload command executed successfully");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"OLE upload failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Upload file using xp_cmdshell with PowerShell -EncodedCommand.
        /// </summary>
        /// <param name="databaseContext">The database context.</param>
        /// <param name="fileContent">The file content as bytes.</param>
        /// <returns>True if upload succeeded; otherwise false.</returns>
        private bool UploadViaXpCmdshell(DatabaseContext databaseContext, byte[] fileContent)
        {
            // Enable xp_cmdshell if needed
            if (!databaseContext.ConfigService.SetConfigurationOption("xp_cmdshell", 1))
            {
                Logger.Error("Failed to enable xp_cmdshell");
                return false;
            }

            Logger.Info("Uploading file via xp_cmdshell (PowerShell -EncodedCommand)");

            // Convert file content to base64 for embedding in PowerShell script
            string fileBase64 = Convert.ToBase64String(fileContent);

            // Escape single quotes in remote path for PowerShell
            string escapedRemotePath = _remotePath.Replace("'", "''");

            // Determine chunk size for large files
            // Encoded command has limits, be conservative
            const int maxChunkSize = 4000; // Conservative limit for base64 data in script

            try
            {
                if (fileBase64.Length <= maxChunkSize)
                {
                    // Small file - single command
                    string psScript = $"$d=[Convert]::FromBase64String('{fileBase64}');[IO.File]::WriteAllBytes('{escapedRemotePath}',$d)";

                    // Encode PowerShell script to base64 UTF-16LE for -EncodedCommand
                    string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(psScript));

                    string query = $"EXEC master..xp_cmdshell 'powershell -e {encodedCommand}'";
                    databaseContext.QueryService.ExecuteNonProcessing(query);
                    Logger.Info("PowerShell upload command executed");
                }
                else
                {
                    // Large file - write in chunks
                    int totalChunks = (fileBase64.Length + maxChunkSize - 1) / maxChunkSize;
                    Logger.Info($"Large file detected, uploading in {totalChunks} chunks");

                    // First chunk - create new file
                    string chunk = fileBase64.Substring(0, Math.Min(maxChunkSize, fileBase64.Length));
                    string psScript = $"$d=[Convert]::FromBase64String('{chunk}');[IO.File]::WriteAllBytes('{escapedRemotePath}',$d)";
                    string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(psScript));
                    string query = $"EXEC master..xp_cmdshell 'powershell -e {encodedCommand}'";
                    databaseContext.QueryService.ExecuteNonProcessing(query);
                    Logger.Info("Chunk 1 uploaded");

                    // Remaining chunks - append
                    int offset = maxChunkSize;
                    int chunkNum = 2;
                    while (offset < fileBase64.Length)
                    {
                        chunk = fileBase64.Substring(offset, Math.Min(maxChunkSize, fileBase64.Length - offset));

                        // Use FileStream to append (compatible with all PS versions)
                        psScript = $"$d=[Convert]::FromBase64String('{chunk}');$f=[IO.File]::Open('{escapedRemotePath}',[IO.FileMode]::Append);$f.Write($d,0,$d.Length);$f.Close()";
                        encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(psScript));
                        query = $"EXEC master..xp_cmdshell 'powershell -e {encodedCommand}'";

                        databaseContext.QueryService.ExecuteNonProcessing(query);

                        offset += maxChunkSize;
                        Logger.Info($"Chunk {chunkNum}/{totalChunks} uploaded");
                        chunkNum++;
                    }
                }

                Logger.Success("PowerShell upload completed");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"PowerShell upload failed: {ex.Message}");
                return false;
            }
        }
    }
}
