using MSSQLand.Actions;
using MSSQLand.Actions.Network;
using MSSQLand.Actions.Database;
using MSSQLand.Actions.FileSystem;
using MSSQLand.Actions.Execution;
using System;
using System.Collections.Generic;

namespace MSSQLand.Utilities
{
    public static class ActionFactory
    {
        private static readonly Dictionary<string, (BaseAction ActionInstance, string Description)> ActionMetadata =
            new()
            {
                { "rows", (new Rows(), "Retrieve rows from a table.") },
                { "query", (new Query(), "Execute a custom SQL query.") },
                { "links", (new Links(), "Retrieve linked server information.") },
                { "xpcmd", (new XpCmd(), "Execute commands using xp_cmdshell.") },
                { "pwsh", (new PowerShell(), "Execute PowerShell commands.") },
                { "pwshdl", (new RemotePowerShellExecutor(), "Download and execute a PowerShell script.") },
                { "read", (new FileRead(), "Read file contents.") },
                { "rpc", (new RemoteProcedureCall(), "Call remote procedures on linked servers.") },
                { "impersonate", (new Impersonation(), "Check and perform user impersonation.") },
                { "info", (new Info(), "Retrieve information about the DBMS server.") },
                { "smb", (new Smb(), "Send SMB requests.") },
                { "users", (new Users(), "List database users.") },
                { "permissions", (new Permissions(), "Enumerate permissions.") },
                { "tables", (new Tables(), "List tables in a database.") },
                { "databases", (new Databases(), "List available databases.") },
            };

        public static BaseAction GetAction(string actionType, string additionalArgument)
        {
            try
            {
                if (!ActionMetadata.TryGetValue(actionType.ToLower(), out var metadata))
                {
                    throw new ArgumentException($"Unsupported action type: {actionType}");
                }

                // Get the action instance
                BaseAction action = metadata.ActionInstance;

                // Validate and initialize the action with the additional argument
                action.ValidateArguments(additionalArgument);
                return action;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error creating action for type '{actionType}': {ex.Message}");
                throw;
            }
        }

        public static Dictionary<string, (BaseAction ActionInstance, string Description)> GetAvailableActions()
        {
            return ActionMetadata;
        }
    }
}
