using MSSQLand.Services;
using MSSQLand.Utilities;
using System;

namespace MSSQLand.Actions.FileSystem
{
    /// <summary>
    /// Read file contents from the SQL Server filesystem using OPENROWSET BULK.
    /// 
    /// Requires ADMINISTER BULK OPERATIONS or ADMINISTER DATABASE BULK OPERATIONS permission.
    /// </summary>
    internal class FileRead : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Full path to the file to read")]
        private string _filePath;

        /// <summary>
        /// Validates and binds the arguments passed to the FileRead action.
        /// </summary>
        /// <param name="args">The action arguments array.</param>
        public override void ValidateArguments(string[] args)
        {
            BindArgumentsToFields(args); // Automatic binding
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

                Console.WriteLine(fileContent);

                return fileContent;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to read file: {ex.Message}");
                return null;
            }
        }
    }
}
