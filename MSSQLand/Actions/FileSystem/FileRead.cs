using MSSQLand.Services;
using MSSQLand.Utilities;
using System;

namespace MSSQLand.Actions.FileSystem
{
    internal class FileRead : BaseAction
    {
        private string _filePath;

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

            _filePath = additionalArguments;
        }

        /// <summary>
        /// Executes the Read action to fetch the content of a file using OPENROWSET.
        /// </summary>
        /// <param name="databaseContext">The ConnectionManager instance to execute the query.</param>
        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Reading file: {_filePath}");

            string query = $@"
                SELECT A 
                FROM OPENROWSET(
                    BULK '{_filePath.Replace("'", "''")}', 
                    SINGLE_CLOB
                ) AS R(A);";

            string fileContent = databaseContext.QueryService.ExecuteScalar(query).ToString();

            Logger.NewLine();

            // Print file content
            Console.WriteLine(fileContent);

            return fileContent;
        }
    }
}
