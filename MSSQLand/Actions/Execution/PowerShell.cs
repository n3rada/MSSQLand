// MSSQLand/Actions/Execution/PowerShell.cs

using MSSQLand.Services;
using MSSQLand.Utilities;

namespace MSSQLand.Actions.Execution
{
    internal class PowerShell : XpCmd
    {

        [ArgumentMetadata(Position = 0, Required = true, Remainder = true, Description = "PowerShell script or command to execute")]
        private string _script = "";

        /// <summary>
        /// Executes the provided PowerShell script on the SQL server.
        /// </summary>
        /// <param name="databaseContext">The ConnectionManager instance to execute the query.</param>
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.Info($"Executing PowerShell script: {_script}");

            _command = $"powershell.exe -NonI -NoPro -of Text -encodedComm {EncodingHelper.ConvertToBase64(_script)}";

            return base.Execute(databaseContext);
        }
    }
}
