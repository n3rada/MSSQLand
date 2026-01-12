// MSSQLand/Actions/Remote/DataAccess.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MSSQLand.Actions.Remote
{
    /// <summary>
    /// Enables or disables data access on a linked server using sp_serveroption.
    /// Data access controls whether OPENQUERY and four-part naming can retrieve data from the linked server.
    /// </summary>
    internal class DataAccess : BaseAction
    {
        private enum DataAccessMode { Enable, Disable }
        
        // Mapping of all accepted aliases to their normalized action
        private static readonly Dictionary<string, DataAccessMode> ActionAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            { "+", DataAccessMode.Enable },
            { "add", DataAccessMode.Enable },
            { "on", DataAccessMode.Enable },
            { "1", DataAccessMode.Enable },
            { "true", DataAccessMode.Enable },
            { "enable", DataAccessMode.Enable },
            { "-", DataAccessMode.Disable },
            { "del", DataAccessMode.Disable },
            { "off", DataAccessMode.Disable },
            { "0", DataAccessMode.Disable },
            { "false", DataAccessMode.Disable },
            { "disable", DataAccessMode.Disable }
        };
        
        [ArgumentMetadata(Position = 0, Required = true, Description = "Action: enable/disable (or aliases: +/-, on/off, 1/0, true/false, add/del)")]
        private DataAccessMode _action;
        
        [ArgumentMetadata(Position = 1, Required = true, Description = "Linked server name")]
        private string _linkedServerName;

        public override void ValidateArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                throw new ArgumentException("Data access action requires two arguments: action and linked server name.");
            }

            if (args.Length != 2)
            {
                throw new ArgumentException("Data access action requires exactly two arguments: action and linked server name.");
            }

            // Map action alias to enum value
            string actionArg = args[0].Trim();
            
            if (!ActionAliases.TryGetValue(actionArg, out _action))
            {
                var enableAliases = string.Join(", ", ActionAliases.Where(kv => kv.Value == DataAccessMode.Enable).Select(kv => $"'{kv.Key}'"));
                var disableAliases = string.Join(", ", ActionAliases.Where(kv => kv.Value == DataAccessMode.Disable).Select(kv => $"'{kv.Key}'"));
                throw new ArgumentException($"Invalid action: '{actionArg}'. Valid actions are: {enableAliases} (to enable) or {disableAliases} (to disable).");
            }

            _linkedServerName = args[1].Trim();
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            string dataAccessValue = _action == DataAccessMode.Enable ? "true" : "false";
            string actionVerb = _action == DataAccessMode.Enable ? "Enabling" : "Disabling";

            Logger.TaskNested($"{actionVerb} data access on linked server '{_linkedServerName}'");

            bool success = databaseContext.ConfigService.SetServerOption(_linkedServerName, "data access", dataAccessValue);

            if (success)
            {
                string status = _action == DataAccessMode.Enable ? "enabled" : "disabled";
                Logger.Success($"Data access successfully {status} on '{_linkedServerName}'");
                if (_action == DataAccessMode.Enable)
                {
                    Logger.SuccessNested("OPENQUERY operations are now available for this server.");
                }
                else
                {
                    Logger.SuccessNested("OPENQUERY operations are no longer available for this server.");
                }
            }
            else
            {
                Logger.Error($"Failed to modify data access settings on '{_linkedServerName}'");
            }

            return success;
        }
    }
}
