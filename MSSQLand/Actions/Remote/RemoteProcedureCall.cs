// MSSQLand/Actions/Remote/RemoteProcedureCall.cs

using MSSQLand.Services;
using MSSQLand.Utilities;

namespace MSSQLand.Actions.Remote
{
    internal class RemoteProcedureCall : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Toggle = true, Description = "Action: enable/disable (or aliases: +/-, on/off, 1/0, true/false, add/del)")]
        private bool _enable = false;

        [ArgumentMetadata(Position = 1, Required = true, Description = "Linked server name")]
        private string _linkedServerName = "";

        public override object Execute(DatabaseContext databaseContext)
        {
            string rpcValue = _enable ? "true" : "false";
            string actionVerb = _enable ? "Enabling" : "Disabling";

            Logger.TaskNested($"{actionVerb} RPC on linked server '{_linkedServerName}'");

            bool success = databaseContext.ConfigService.SetServerOption(_linkedServerName, "rpc out", rpcValue);

            if (success)
            {
                string status = _enable ? "enabled" : "disabled";
                Logger.Success($"RPC successfully {status} on '{_linkedServerName}'");
            }
            else
            {
                Logger.Error($"Failed to modify RPC settings on '{_linkedServerName}'");
            }

            return success;
        }
    }
}
