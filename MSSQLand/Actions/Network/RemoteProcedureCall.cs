using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;
using System.Linq;

namespace MSSQLand.Actions.Network
{
    internal class RemoteProcedureCall : BaseAction
    {
        private enum RpcActionMode { Add, Del }
        
        [ArgumentMetadata(Position = 0, Required = true, Description = "Action: add or del")]
        private RpcActionMode _action;
        
        [ArgumentMetadata(Position = 1, Required = true, Description = "Linked server name")]
        private string _linkedServerName;

        public override void ValidateArguments(string additionalArguments)
        {
            if (string.IsNullOrWhiteSpace(additionalArguments))
            {
                throw new ArgumentException("Remote Procedure Call (RPC) action requires two arguments: action ('add' or 'del') and linked server name.");
            }

            string[] args = SplitArguments(additionalArguments);

            if (args.Length != 2)
            {
                throw new ArgumentException("RPC action requires exactly two arguments: action ('add' or 'del') and linked server name.");
            }

            // Parse action mode
            if (!Enum.TryParse(args[0].Trim(), true, out _action))
            {
                string validActions = string.Join(", ", Enum.GetNames(typeof(RpcActionMode)).Select(a => a.ToLower()));
                throw new ArgumentException($"Invalid action: {args[0]}. Valid actions are: {validActions}.");
            }

            _linkedServerName = args[1].Trim();
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            string rpcValue = _action == RpcActionMode.Add ? "true" : "false";

            Logger.TaskNested($"Executing RPC {_action.ToString().ToLower()} on linked server '{_linkedServerName}'");

            string query = $@"
                EXEC sp_serveroption 
                     @server = '{_linkedServerName}', 
                     @optname = 'rpc out', 
                     @optvalue = '{rpcValue}';
            ";

            try
            {
                DataTable resultTable = databaseContext.QueryService.ExecuteTable(query);
                Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(resultTable));

                Logger.Success($"RPC {_action.ToString().ToLower()} action executed successfully on '{_linkedServerName}'");
                return resultTable;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to execute RPC {_action.ToString().ToLower()} on '{_linkedServerName}': {ex.Message}");
                return null;
            }
        }
    }
}
