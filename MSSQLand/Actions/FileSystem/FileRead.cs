using MSSQLand.Services;
using MSSQLand.Utilities;
using System;

namespace MSSQLand.Actions.FileSystem
{
    /// <summary>
    /// Read file contents from the SQL Server filesystem using OPENROWSET BULK.
    /// 
    /// Requires ADMINISTER BULK OPERATIONS or ADMINISTER DATABASE BULK OPERATIONS permission.
    /// 
    /// Usage:
    /// - /a:fileread C:\Windows\win.ini
    /// - /a:fileread C:\temp\file.txt
    /// </summary>
    internal class FileRead : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Full path to the file to read")]
        private string _filePath;

        /// <summary>
        /// Validates the arguments passed to the Read action.
        /// </summary>
        /// <param name="additionalArguments">The file path to read.</param>
        public override void ValidateArguments(string additionalArguments)
        {
            if (string.IsNullOrEmpty(additionalArguments))
            {
                throw new ArgumentException("FileRead action requires a file path as an argument.");
            }

            // Parse both positional and named arguments
            var (namedArgs, positionalArgs) = ParseArguments(additionalArguments);

            // Get file path from position 0
            _filePath = GetPositionalArgument(positionalArgs, 0);

            if (string.IsNullOrEmpty(_filePath))
            {
                throw new ArgumentException("FileRead action requires a file path as an argument.");
            }
        }

        /// <summary>
        /// Executes the Read action to fetch the content of a file using OPENROWSET BULK.
        /// </summary>
        /// <param name="databaseContext">The ConnectionManager instance to execute the query.</param>
        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Reading file: {_filePath}");

            try
            {
                string query = $@"SELECT A FROM OPENROWSET(BULK '{_filePath.Replace("'", "''")}', SINGLE_CLOB) AS R(A);";
                string fileContent = databaseContext.QueryService.ExecuteScalar(query)?.ToString();

                Logger.NewLine();
                Console.WriteLine(fileContent);

                return fileContent;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to read file: {ex.Message}");
                Logger.NewLine();
                Logger.Info("OPENROWSET BULK requires one of:");
                Logger.InfoNested("- ADMINISTER BULK OPERATIONS (server-level permission)");
                Logger.InfoNested("- ADMINISTER DATABASE BULK OPERATIONS (database-level permission)");
                Logger.NewLine();
                Logger.Info("Verify that:");
                Logger.InfoNested("- The file exists and path is correct");
                Logger.InfoNested("- SQL Server service account has read access to the file");
                Logger.InfoNested("- You have the required BULK OPERATIONS permissions");
                
                return null;
            }
        }
    }
}
