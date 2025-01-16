using System;
using System.Collections.Generic;
using MSSQLand.Actions;
using MSSQLand.Actions.Network;
using MSSQLand.Actions.Database;
using MSSQLand.Actions.FileSystem;
using MSSQLand.Actions.Execution;
using MSSQLand.Actions.Administration;

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
                { "config", (new Configure(), "Use sp_configure to modify settings.") },
                { "search", (new Search(), "Search for specific keyword in database.") },
                { "ole", (new ObjectLinkingEmbedding(), "Executes the specified command using OLE Automation Procedures.") },
                { "clr", (new ClrExecution(), "Execute commands using Common Language Runtime (CLR) assemblies.") }
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

        public static List<(string ActionName, string Description, string Arguments)> GetAvailableActions()
        {
            var result = new List<(string ActionName, string Description, string Arguments)>();

            foreach (var action in ActionMetadata)
            {
                string arguments = action.Value.ActionInstance.GetArguments();
                result.Add((action.Key, action.Value.Description, arguments));
            }

            return result;
        }
    }
}
