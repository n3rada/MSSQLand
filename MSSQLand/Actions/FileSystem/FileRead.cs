// MSSQLand/Actions/FileSystem/FileRead.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using System;

namespace MSSQLand.Actions.FileSystem
{
    /// <summary>
    /// Read file contents from the SQL Server filesystem using OPENROWSET BULK.
    /// 
    /// Requires ADMINISTER BULK OPERATIONS or ADMINISTER DATABASE BULK OPERATIONS permission,
    /// or membership in the bulk_admin server role.
    /// </summary>
    internal class FileRead : BaseAction
    {
        // Default to a file that always exists and is readable on Windows
        private const string DefaultFilePath = @"C:\Windows\win.ini";

        [ArgumentMetadata(Position = 0, Required = false, Description = "Full path to the file to read (default: C:\\Windows\\win.ini)")]
        private string _filePath = DefaultFilePath;

        /// <summary>
        /// Executes the Read action to fetch the content of a file using OPENROWSET BULK.
        /// </summary>
        /// <param name="databaseContext">The ConnectionManager instance to execute the query.</param>
        public override object Execute(DatabaseContext databaseContext)
        {
            if (string.IsNullOrWhiteSpace(_filePath))
            {
                _filePath = DefaultFilePath;
            }

            bool isDefaultFile = _filePath.Equals(DefaultFilePath, StringComparison.OrdinalIgnoreCase);
            
            if (isDefaultFile)
            {
                Logger.TaskNested($"Testing OPENROWSET BULK access with default file: {_filePath}");
            }
            else
            {
                Logger.TaskNested($"Reading file: {_filePath}");
            }

            try
            {
                string query = $@"SELECT A FROM OPENROWSET(BULK '{_filePath.Replace("'", "''")}', SINGLE_CLOB) AS R(A);";
                string fileContent = databaseContext.QueryService.ExecuteScalar(query)?.ToString();

                Console.WriteLine(fileContent);

                return fileContent;
            }
            catch (Exception ex)
            {
                if (ex.Message.IndexOf("ADMINISTER BULK OPERATIONS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    ex.Message.IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Logger.Error("No BULK access - requires bulk_admin role or ADMINISTER BULK OPERATIONS permission");
                }
                else if (ex.Message.IndexOf("Could not find", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         ex.Message.IndexOf("Cannot open", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Logger.Error($"File not found or access denied: {_filePath}");
                }
                else
                {
                    Logger.Error($"Failed to read file: {ex.Message}");
                }
                return null;
            }
        }
    }
}
