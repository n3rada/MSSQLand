using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;
using System.Linq;

namespace MSSQLand.Actions.Remote
{
    internal class RemoteProcedureCall : BaseAction
    {
        private enum RpcActionMode { Add, Del }
        
        [ArgumentMetadata(Position = 0, Required = true, Description = "Action: + or -")]
        private RpcActionMode _action;
        
        [ArgumentMetadata(Position = 1, Required = true, Description = "Linked server name")]
        private string _linkedServerName;

        public override void ValidateArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                throw new ArgumentException("Remote Procedure Call (RPC) action requires two arguments: action ('+' or '-') and linked server name.");
            }

            if (args.Length != 2)
            {
                throw new ArgumentException("RPC action requires exactly two arguments: action ('+' or '-') and linked server name.");
            }

            // Map symbols to enum values
            string actionArg = args[0].Trim();
            if (actionArg == "+")
            {
                _action = RpcActionMode.Add;
            }
            else if (actionArg == "-")
            {
                _action = RpcActionMode.Del;
            }
            else
            {
                throw new ArgumentException($"Invalid action: {args[0]}. Valid actions are: '+' (enable) or '-' (disable).");
            }

            _linkedServerName = args[1].Trim();
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            string rpcValue = _action == RpcActionMode.Add ? "true" : "false";
            string actionVerb = _action == RpcActionMode.Add ? "Enabling" : "Disabling";

            Logger.TaskNested($"{actionVerb} RPC on linked server '{_linkedServerName}'");

            string query = $@"
                EXEC sp_serveroption 
                     @server = '{_linkedServerName}', 
                     @optname = 'rpc out', 
                     @optvalue = '{rpcValue}';
            ";

            try
            {
                DataTable resultTable = databaseContext.QueryService.ExecuteTable(query);
                Console.WriteLine(OutputFormatter.ConvertDataTable(resultTable));

                string status = _action == RpcActionMode.Add ? "enabled" : "disabled";
                Logger.Success($"RPC successfully {status} on '{_linkedServerName}'");
                return resultTable;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to modify RPC settings on '{_linkedServerName}': {ex.Message}");
                return null;
            }
        }
    }
}
