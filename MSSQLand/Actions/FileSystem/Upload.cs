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
    /// remote path on the SQL Server using OLE Automation (ADODB.Stream).
    /// After upload, it verifies the file was created.
    ///
    /// Method used: OLE Automation with ADODB.Stream (handles binary data well)
    /// </summary>
    internal class Upload : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Local file path to upload")]
        private string _localPath = "";

        [ArgumentMetadata(Position = 1, Description = @"Remote destination path (defaults to C:\Windows\Tasks\)")]
        private string _remotePath = "";

        private FileInfo _localFileInfo;

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);

            // Expand environment variables and resolve path
            _localPath = Environment.ExpandEnvironmentVariables(_localPath);

            // Validate local file exists and is non-empty
            _localFileInfo = new FileInfo(_localPath);
            if (!_localFileInfo.Exists)
            {
                throw new ArgumentException($"Local file does not exist: {_localPath}");
            }

            if (_localFileInfo.Length == 0)
            {
                throw new ArgumentException($"Local file is empty: {_localPath}");
            }

            // Clean up remote path if provided
            if (!string.IsNullOrWhiteSpace(_remotePath))
            {
                // Remove trailing quotes (PowerShell escaping issue: "path\" becomes path")
                _remotePath = _remotePath.TrimEnd('"');

                // Normalize path separators
                _remotePath = _remotePath.Replace("/", "\\");
            }
        }

        /// <summary>
        /// Executes the upload action.
        /// </summary>
        /// <param name="databaseContext">The DatabaseContext instance to execute the query.</param>
        public override object Execute(DatabaseContext databaseContext)
        {

            Logger.Info($"Uploading file to SQL Server");

            if (string.IsNullOrWhiteSpace(_remotePath))
            {
                _remotePath = $"C:\\Windows\\Tasks\\{_localFileInfo.Name}";
                Logger.InfoNested($"No remote path specified, using default: {_remotePath}");
            }
            else if (_remotePath.EndsWith("\\") || !Path.HasExtension(_remotePath))
            {
                // Path is a directory (ends with \ or has no extension)
                _remotePath = Path.Combine(_remotePath, _localFileInfo.Name);
                Logger.InfoNested($"Remote path is a directory, appending filename");
            }

            Logger.InfoNested($"Local file: {_localFileInfo.FullName}");
            Logger.InfoNested($"File size: {_localFileInfo.Length:N0} bytes");
            Logger.InfoNested($"Remote destination: {_remotePath}");

            // Read local file content
            byte[] fileContent;
            try
            {
                fileContent = File.ReadAllBytes(_localFileInfo.FullName);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to read local file: {ex.Message}");
                return false;
            }

            // Try to enable OLE Automation
            bool oleAvailable = databaseContext.ConfigService.SetConfigurationOption("Ole Automation Procedures", 1);

            if (!oleAvailable)
            {
                Logger.Error("Cannot enable OLE Automation (no ALTER SETTINGS permission)");
                return false;
            }

            Logger.Info("OLE Automation is available, using OLE method");
            bool success = UploadViaOle(databaseContext, fileContent);

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
                Logger.Warning("File too large for OLE method (>2GB)");
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
                Logger.Success("OLE upload command executed successfully");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"OLE upload failed: {ex.Message}");
                return false;
            }
        }

    }
}
