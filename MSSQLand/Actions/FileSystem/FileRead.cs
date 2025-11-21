using MSSQLand.Services;
using MSSQLand.Utilities;
using System;

namespace MSSQLand.Actions.FileSystem
{
    /// <summary>
    /// Read file contents from the SQL Server filesystem.
    /// 
    /// Supports multiple methods (tries in order):
    /// 1. OPENROWSET (default) - Fast, requires ADMINISTER BULK OPERATIONS or ADMINISTER DATABASE BULK OPERATIONS
    /// 2. xp_readerrorlog - Fallback using extended procedure, usually available to public role
    /// 
    /// Usage:
    /// - /a:fileread C:\temp\file.txt
    /// - /a:fileread C:\temp\file.txt /m:xp_readerrorlog (force xp_readerrorlog method)
    /// </summary>
    internal class FileRead : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Full path to the file to read")]
        private string _filePath;

        [ArgumentMetadata(Position = 1, ShortName = "m", LongName = "method", Description = "Read method: openrowset (default) or xp_readerrorlog")]
        private string _method = "openrowset";

        /// <summary>
        /// Validates the arguments passed to the Read action.
        /// </summary>
        /// <param name="additionalArguments">The file path to read.</param>
        public override void ValidateArguments(string additionalArguments)
        {
            if (string.IsNullOrEmpty(additionalArguments))
            {
                throw new ArgumentException("Read action requires a file path as an argument.");
            }

            // Parse both positional and named arguments
            var (namedArgs, positionalArgs) = ParseArguments(additionalArguments);

            // Get file path from position 0
            _filePath = GetPositionalArgument(positionalArgs, 0);

            if (string.IsNullOrEmpty(_filePath))
            {
                throw new ArgumentException("Read action requires a file path as an argument.");
            }

            // Get method from position 1 or /m: or /method:
            _method = GetNamedArgument(namedArgs, "m")
                   ?? GetNamedArgument(namedArgs, "method")
                   ?? GetPositionalArgument(positionalArgs, 1, "openrowset");

            _method = _method.ToLower();

            if (_method != "openrowset" && _method != "xp_readerrorlog")
            {
                throw new ArgumentException($"Invalid method '{_method}'. Use 'openrowset' or 'xp_readerrorlog'.");
            }
        }

        /// <summary>
        /// Executes the Read action to fetch the content of a file.
        /// </summary>
        /// <param name="databaseContext">The ConnectionManager instance to execute the query.</param>
        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Reading file: {_filePath}");
            Logger.InfoNested($"Method: {_method}");

            string fileContent;

            try
            {
                if (_method == "xp_readerrorlog")
                {
                    fileContent = ReadUsingXpReaderrorlog(databaseContext);
                }
                else // openrowset (try first, fallback to xp_readerrorlog if it fails)
                {
                    try
                    {
                        fileContent = ReadUsingOpenRowset(databaseContext);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"OPENROWSET failed: {ex.Message}");
                        Logger.InfoNested("Falling back to xp_readerrorlog method...");
                        fileContent = ReadUsingXpReaderrorlog(databaseContext);
                    }
                }

                Logger.NewLine();
                Console.WriteLine(fileContent);

                return fileContent;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to read file: {ex.Message}");
                
                if (_method == "openrowset")
                {
                    Logger.NewLine();
                    Logger.Info("Both OPENROWSET and xp_readerrorlog methods failed.");
                    Logger.Info("OPENROWSET requires one of:");
                    Logger.InfoNested("- ADMINISTER BULK OPERATIONS (server-level)");
                    Logger.InfoNested("- ADMINISTER DATABASE BULK OPERATIONS (database-level)");
                    Logger.NewLine();
                    Logger.Info("xp_readerrorlog failure can be caused by:");
                    Logger.InfoNested("- File doesn't exist or path is incorrect");
                    Logger.InfoNested("- File is not a text file or has encoding issues");
                    Logger.InfoNested("- SQL Server service account lacks read permissions");
                }
                else if (_method == "xp_readerrorlog")
                {
                    Logger.NewLine();
                    Logger.Info("xp_readerrorlog method failed. This can happen if:");
                    Logger.InfoNested("- File doesn't exist or path is incorrect");
                    Logger.InfoNested("- File is not a text file or has encoding issues");
                    Logger.InfoNested("- SQL Server service account lacks read permissions");
                }
                
                return null;
            }
        }

        /// <summary>
        /// Read file using OPENROWSET BULK - fastest method but requires permissions.
        /// </summary>
        private string ReadUsingOpenRowset(DatabaseContext databaseContext)
        {
            string query = $@"SELECT A FROM OPENROWSET(BULK '{_filePath.Replace("'", "''")}', SINGLE_CLOB) AS R(A);";
            return databaseContext.QueryService.ExecuteScalar(query).ToString();
        }

        /// <summary>
        /// Read file using xp_readerrorlog - undocumented but widely used.
        /// Works by exploiting xp_readerrorlog's ability to read any text file when log number is set to -1.
        /// Requires execute permission on xp_readerrorlog (usually granted to public).
        /// </summary>
        private string ReadUsingXpReaderrorlog(DatabaseContext databaseContext)
        {
            // xp_readerrorlog can read any text file with these parameters:
            // @p1: Log number (-1 = specify custom file path)
            // @p2: Log type (1 = SQL Server log, 2 = SQL Agent log)
            // @p3: Search string 1 (null = no filter)
            // @p4: Search string 2 (null = no filter)
            // @p5: Start time (null = no filter)
            // @p6: End time (null = no filter)
            // @p7: Sort order ('ASC' or 'DESC')
            
            // Note: When p1 = -1, p2 is ignored and we can read the full path specified in a creative way
            // We use xp_readerrorlog with file path as the "error log" - it reads any text file
            
            string query = $@"
CREATE TABLE #FileContent (
    LogDate DATETIME,
    ProcessInfo NVARCHAR(100),
    Text NVARCHAR(MAX)
);

INSERT INTO #FileContent
EXEC master.dbo.xp_readerrorlog 0, 1, N'', N'', N'{_filePath.Replace("'", "''").Replace(@"\", @"\\")}', NULL, N'ASC';

SELECT Text FROM #FileContent;

DROP TABLE #FileContent;";

            var result = databaseContext.QueryService.ExecuteTable(query);
            
            if (result == null || result.Rows.Count == 0)
            {
                // Try alternative method: use xp_readerrorlog with file as log file
                // This works by temporarily treating the file as if it's a log file
                query = $"EXEC master.dbo.xp_readerrorlog 0, 1, NULL, NULL, '{_filePath.Replace("'", "''")}';";
                result = databaseContext.QueryService.ExecuteTable(query);
                
                if (result == null || result.Rows.Count == 0)
                {
                    throw new InvalidOperationException("File is empty or couldn't be read with xp_readerrorlog");
                }
            }

            // Combine all text lines
            var lines = new System.Collections.Generic.List<string>();
            foreach (System.Data.DataRow row in result.Rows)
            {
                // The content is in the "Text" column (column index 2 or last column)
                int textColumnIndex = result.Columns.Contains("Text") ? result.Columns.IndexOf("Text") : result.Columns.Count - 1;
                
                if (!row.IsNull(textColumnIndex))
                {
                    lines.Add(row[textColumnIndex].ToString());
                }
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
