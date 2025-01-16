using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;


namespace MSSQLand.Actions.Administration
{
    internal class RemoteProcedureCall : BaseAction
    {
        private string _action;
        private string _linkedServerName;

        public override void ValidateArguments(string additionalArguments)
        {
            if (string.IsNullOrEmpty(additionalArguments))
            {
                throw new ArgumentException("Remote Procedure Call (RPC) action requires exactly two arguments: action ('add' or 'del') and linked server name.");
            }

            string[] args = SplitArguments(additionalArguments);


            if (args.Length != 2)
            {
                throw new ArgumentException("RPC action requires exactly two arguments: action ('add' or 'del') and linked server name.");
            }

            _action = args[0].ToLower();
            _linkedServerName = args[1];

            if (_action != "add" && _action != "del")
            {
                throw new ArgumentException($"Unsupported action: {_action}. Supported actions are 'add' or 'del'.");
            }
        }

        public override void Execute(DatabaseContext connectionManager)
        {
            string rpcValue = _action == "add" ? "true" : "false";


            Logger.TaskNested($"Remote Procedure Call (RPC) action: {_action}");
            Logger.TaskNested($"On {_linkedServerName}");


            string query = $"EXEC sp_serveroption @server = '{_linkedServerName}', @optname = 'rpc out', @optvalue = '{rpcValue}';";

            DataTable resultTable = connectionManager.QueryService.ExecuteTable(query);
            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(resultTable));
        }
    }

}
