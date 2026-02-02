// MSSQLand/Actions/Remote/RemoteProcedureCall.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using System;

namespace MSSQLand.Actions.Remote
{
    internal class RemoteProcedureCall : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Action: enable/disable (or aliases: +/-, on/off, 1/0, true/false, add/del)")]
        private bool _enable;

        [ArgumentMetadata(Position = 1, Required = true, Description = "Linked server name")]
        private string _linkedServerName = "";

        public override void ValidateArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                throw new ArgumentException("Remote Procedure Call (RPC) action requires two arguments: action and linked server name.");
            }

            if (args.Length != 2)
            {
                throw new ArgumentException("RPC action requires exactly two arguments: action and linked server name.");
            }

            if (!TryParseToggleAction(args[0], out bool enable, out string error))
            {
                throw new ArgumentException(error);
            }

            var normalizedArgs = (string[])args.Clone();
            normalizedArgs[0] = enable ? "true" : "false";

            BindArguments(normalizedArgs);
        }

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
