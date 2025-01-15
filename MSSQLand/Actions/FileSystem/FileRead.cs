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
        /// <param name="additionalArgument">The file path to read.</param>
        public override void ValidateArguments(string additionalArgument)
        {
            if (string.IsNullOrEmpty(additionalArgument))
            {
                throw new ArgumentException("Read action requires a file path as an argument.");
            }

            _filePath = additionalArgument;
        }

        /// <summary>
        /// Executes the Read action to fetch the content of a file using OPENROWSET.
        /// </summary>
        /// <param name="connectionManager">The ConnectionManager instance to execute the query.</param>
        public override void Execute(DatabaseContext connectionManager)
        {
            Logger.TaskNested($"Reading file: {_filePath}");

            string query = $@"
                SELECT A 
                FROM OPENROWSET(
                    BULK '{_filePath.Replace("'", "''")}', 
                    SINGLE_CLOB
                ) AS R(A);";


            Console.WriteLine(connectionManager.QueryService.ExecuteScalar(query).ToString());
        }
    }
}
