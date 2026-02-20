// MSSQLand/Actions/FileSystem/RemoveFile.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using System;

namespace MSSQLand.Actions.FileSystem
{
    /// <summary>
    /// Delete a file on the SQL Server filesystem using OLE Automation (Scripting.FileSystemObject).
    ///
    /// Attempts deletion directly. If OLE Automation is disabled (sp_OACreate blocked),
    /// enables it and retries once.
    ///
    /// Examples:
    ///   rm C:\temp\payload.exe
    ///   rm "C:\Program Files\data\export.csv"
    /// </summary>
    internal class RemoveFile : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Remote file path to delete")]
        private string _filePath = "";

        private bool _oleAttemptedEnable = false;

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);
            _filePath = _filePath.Replace("/", "\\");
        }

        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Deleting remote file: {_filePath}");

            bool success = DeleteViaOle(databaseContext);

            if (!success)
            {
                return false;
            }

            Logger.Success($"File deleted");
            return true;
        }

        /// <summary>
        /// Delete file using OLE Automation with Scripting.FileSystemObject.
        /// If OLE is disabled (sp_OACreate blocked), attempts to enable it and retries once.
        /// </summary>
        private bool DeleteViaOle(DatabaseContext databaseContext)
        {
            string escapedPath = _filePath.Replace("'", "''");

            string query = $@"
DECLARE @ObjectToken INT;
DECLARE @Result INT;
DECLARE @ErrorSource NVARCHAR(255);
DECLARE @ErrorDesc NVARCHAR(255);

EXEC @Result = sp_OACreate 'Scripting.FileSystemObject', @ObjectToken OUTPUT;
IF @Result <> 0
BEGIN
    EXEC sp_OAGetErrorInfo @ObjectToken, @ErrorSource OUT, @ErrorDesc OUT;
    RAISERROR('Failed to create FileSystemObject: %s', 16, 1, @ErrorDesc);
    RETURN;
END

EXEC @Result = sp_OAMethod @ObjectToken, 'DeleteFile', NULL, '{escapedPath}';
IF @Result <> 0
BEGIN
    EXEC sp_OAGetErrorInfo @ObjectToken, @ErrorSource OUT, @ErrorDesc OUT;
    EXEC sp_OADestroy @ObjectToken;
    RAISERROR('Failed to delete file: %s', 16, 1, @ErrorDesc);
    RETURN;
END

EXEC sp_OADestroy @ObjectToken;
";

            try
            {
                databaseContext.QueryService.ExecuteNonProcessing(query);
                return true;
            }
            catch (Exception ex)
            {
                // Only retry if OLE Automation itself is blocked
                if (!_oleAttemptedEnable && ex.Message.Contains("sp_OACreate"))
                {
                    _oleAttemptedEnable = true;
                    Logger.Info("OLE Automation is disabled, attempting to enable it");

                    if (!databaseContext.ConfigService.SetConfigurationOption("Ole Automation Procedures", 1))
                    {
                        Logger.Error("Cannot enable OLE Automation (no ALTER SETTINGS permission)");
                        return false;
                    }

                    try
                    {
                        databaseContext.QueryService.ExecuteNonProcessing(query);
                        return true;
                    }
                    catch (Exception retryEx)
                    {
                        Logger.Error($"Deletion failed: {retryEx.Message}");
                        return false;
                    }
                }

                Logger.Error($"Deletion failed: {ex.Message}");
                return false;
            }
        }
    }
}
