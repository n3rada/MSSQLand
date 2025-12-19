// MSSQLand/Actions/Remote/RemoteProcedureCall.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace MSSQLand.Actions.Remote
{
    internal class RemoteProcedureCall : BaseAction
    {
        private enum RpcActionMode { Enable, Disable }
        
        // Mapping of all accepted aliases to their normalized action
        private static readonly Dictionary<string, RpcActionMode> ActionAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            { "+", RpcActionMode.Enable },
            { "add", RpcActionMode.Enable },
            { "on", RpcActionMode.Enable },
            { "1", RpcActionMode.Enable },
            { "true", RpcActionMode.Enable },
            { "enable", RpcActionMode.Enable },
            { "-", RpcActionMode.Disable },
            { "del", RpcActionMode.Disable },
            { "off", RpcActionMode.Disable },
            { "0", RpcActionMode.Disable },
            { "false", RpcActionMode.Disable },
            { "disable", RpcActionMode.Disable }
        };
        
        [ArgumentMetadata(Position = 0, Required = true, Description = "Action: enable/disable (or aliases: +/-, on/off, 1/0, true/false, add/del)")]
        private RpcActionMode _action;
        
        [ArgumentMetadata(Position = 1, Required = true, Description = "Linked server name")]
        private string _linkedServerName;

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

            // Map action alias to enum value
            string actionArg = args[0].Trim();
            
            if (!ActionAliases.TryGetValue(actionArg, out _action))
            {
                var enableAliases = string.Join(", ", ActionAliases.Where(kv => kv.Value == RpcActionMode.Enable).Select(kv => $"'{kv.Key}'"));
                var disableAliases = string.Join(", ", ActionAliases.Where(kv => kv.Value == RpcActionMode.Disable).Select(kv => $"'{kv.Key}'"));
                throw new ArgumentException($"Invalid action: '{actionArg}'. Valid actions are: {enableAliases} (to enable) or {disableAliases} (to disable).");
            }

            _linkedServerName = args[1].Trim();
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            string rpcValue = _action == RpcActionMode.Enable ? "true" : "false";
            string actionVerb = _action == RpcActionMode.Enable ? "Enabling" : "Disabling";

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

                string status = _action == RpcActionMode.Enable ? "enabled" : "disabled";
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
