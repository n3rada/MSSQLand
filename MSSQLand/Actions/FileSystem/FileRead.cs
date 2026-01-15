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

        [ArgumentMetadata(Named = "b64", Description = "Output file content as base64 encoded (useful for binary files)")]
        private bool _base64 = false;

        public override void ValidateArguments(string[] args)
        {
            var (namedArgs, positionalArgs) = ParseActionArguments(args);

            _filePath = GetPositionalArgument(positionalArgs, 0, DefaultFilePath);
            if (string.IsNullOrWhiteSpace(_filePath))
            {
                _filePath = DefaultFilePath;
            }

            _base64 = namedArgs.ContainsKey("b64") || namedArgs.ContainsKey("base64");
        }

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

            Logger.TaskNested($"Reading file: {_filePath}");
            if (_base64)
            {
                Logger.Info("Output: base64 encoded");
            }

            try
            {
                string escapedPath = _filePath.Replace("'", "''");
                string output;

                if (_base64)
                {
                    // Read as binary and convert to base64 in SQL
                    string query = $@"
SELECT CAST('' AS XML).value('xs:base64Binary(sql:column(""B""))', 'VARCHAR(MAX)')
FROM (SELECT A AS B FROM OPENROWSET(BULK '{escapedPath}', SINGLE_BLOB) AS R(A)) AS T;";
                    output = databaseContext.QueryService.ExecuteScalar(query)?.ToString();
                }
                else
                {
                    // Read as text (original behavior)
                    string query = $@"SELECT A FROM OPENROWSET(BULK '{escapedPath}', SINGLE_CLOB) AS R(A);";
                    output = databaseContext.QueryService.ExecuteScalar(query)?.ToString();
                }

                Logger.NewLine();
                Console.WriteLine(output);

                return output;
            }
            catch (Exception ex)
            {
                if (ex.Message.IndexOf("ADMINISTER BULK OPERATIONS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    ex.Message.IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Logger.Error("No BULK access - requires bulk_admin role or ADMINISTER BULK OPERATIONS permission");
                }
                else if (ex.Message.IndexOf("Could not find", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         ex.Message.IndexOf("cannot find the path", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         ex.Message.IndexOf("error code 3", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         ex.Message.IndexOf("error code 2", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         ex.Message.IndexOf("Cannot open", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Logger.Error($"File or path not found: {_filePath}");
                }
                else if (ex.Message.IndexOf("error code 5", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         ex.Message.IndexOf("Access is denied", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Logger.Error($"Access denied: {_filePath}");
                    Logger.WarningNested("SQL Server service account does not have read access to this file");
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
