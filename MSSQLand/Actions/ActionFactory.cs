using MSSQLand.Actions;
using MSSQLand.Actions.Network;
using MSSQLand.Actions.Database;
using MSSQLand.Actions.FileSystem;
using MSSQLand.Actions.Execution;
using System;


namespace MSSQLand.Utilities
{
    public static class ActionFactory
    {
        public static BaseAction GetAction(string actionType, string additionalArgument)
        {
            try
            {
                // Determine the appropriate action type
                BaseAction action = actionType.ToLower() switch
                {
                    "rows" => new Rows(),
                    "query" => new Query(),
                    "links" => new Links(),
                    "xpcmd" => new XpCmd(),
                    "pwsh" => new PowerShell(),
                    "pwshdl" => new RemotePowerShellExecutor(),
                    "read" => new FileRead(),
                    "rpc" => new RemoteProcedureCall(),
                    "impersonate" => new Impersonation(),
                    "info" => new Info(),
                    "smb" => new Smb(),
                    "users" => new Users(),
                    "permissions" => new Permissions(),
                    "tables" => new Tables(),
                    "databases" => new Databases(),
                    _ => throw new ArgumentException($"Unsupported action type: {actionType}")
                };

                // Validate and initialize the action with the additional argument
                action.ValidateArguments(additionalArgument);
                return action;
            }
            catch (Exception ex)
            {
                // Log the exception details
                Logger.Error($"Error creating action for type '{actionType}': {ex.Message}");

                // Re-raise the exception to ensure it propagates upwards
                throw;
            }
        }
    }
}
