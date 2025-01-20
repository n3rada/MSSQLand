using System;
using System.Collections.Generic;
using MSSQLand.Actions;
using MSSQLand.Actions.Network;
using MSSQLand.Actions.Database;
using MSSQLand.Actions.FileSystem;
using MSSQLand.Actions.Execution;
using MSSQLand.Actions.Administration;
using MSSQLand.Actions.Enumeration;

namespace MSSQLand.Utilities
{
    public static class ActionFactory
    {
        private static readonly Dictionary<string, (Type ActionClass, string Description)> ActionMetadata =
            new()
            {
                { "rows", (typeof(Rows), "Retrieve rows from a table.") },
                { "query", (typeof(Query), "Execute a custom SQL query.") },
                { "links", (typeof(Links), "Retrieve linked server information.") },
                { "exec", (typeof(XpCmd), "Execute commands using xp_cmdshell.") },
                { "pwsh", (typeof(PowerShell), "Execute PowerShell commands.") },
                { "pwshdl", (typeof(RemotePowerShellExecutor), "Download and execute a PowerShell script.") },
                { "read", (typeof(FileRead), "Read file contents.") },
                { "rpc", (typeof(RemoteProcedureCall), "Call remote procedures on linked servers.") },
                { "impersonate", (typeof(Impersonation), "Check and perform user impersonation.") },
                { "info", (typeof(Info), "Retrieve information about the DBMS server.") },
                { "whoami", (typeof(Whoami), "Retrieve information about the current user.") },
                { "smb", (typeof(Smb), "Send SMB requests.") },
                { "users", (typeof(Users), "List database users.") },
                { "permissions", (typeof(Permissions), "Enumerate permissions.") },
                { "procedures", (typeof(Procedures), "List available procedures.") },
                { "tables", (typeof(Tables), "List tables in a database.") },
                { "databases", (typeof(Databases), "List available databases.") },
                { "config", (typeof(Configure), "Use sp_configure to modify settings.") },
                { "search", (typeof(Search), "Search for specific keyword in database.") },
                { "ole", (typeof(ObjectLinkingEmbedding), "Executes the specified command using OLE Automation Procedures.") },
                { "clr", (typeof(ClrExecution), "Deploy and execute CLR assemblies.") },
                { "agents", (typeof(Agents), "Interact with and manage SQL Server Agent jobs.") },
                { "adsi-creds", (typeof(AdsiCredentialExtractor), "Extract credentials by querying your own LDAP server using the ADSI provider.") },
                { "monitor", (typeof(Monitor), "List running SQL commands.") },
                { "kill", (typeof(Kill), "Terminate running SQL commands by session ID or all.") },
                { "oledb-providers", (typeof(OleDbProvidersInfo), "Retrieve detailed configuration and properties of OLE DB providers.") }
            };

        private static readonly Dictionary<string, (Type ActionClass, string Description)> EnumerationMetadata =
            new()
            {
                { "servers", (typeof(FindSQLServers), "Search for MS SQL Servers.") }
            };

        public static BaseAction GetAction(string actionType, string additionalArguments)
        {
            try
            {
                if (!ActionMetadata.TryGetValue(actionType.ToLower(), out var metadata))
                {
                    throw new ArgumentException($"Unsupported action type: {actionType}");
                }

                // Create an instance of the action class
                BaseAction action = (BaseAction)Activator.CreateInstance(metadata.ActionClass);

                // Validate and initialize the action with the additional argument
                action.ValidateArguments(additionalArguments);
                return action;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error creating action for type '{actionType}': {ex.Message}");
                throw;
            }
        }

        public static BaseAction GetEnumeration(string enumType, string additionalArguments)
        {
            try
            {
                if (!EnumerationMetadata.TryGetValue(enumType.ToLower(), out var metadata))
                {
                    throw new ArgumentException($"Unsupported enum type: {enumType}");
                }

                // Create an instance of the enumeration class
                BaseAction action = (BaseAction)Activator.CreateInstance(metadata.ActionClass);

                // Validate and initialize the action with the additional argument
                action.ValidateArguments(additionalArguments);

                return action;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error creating enumeration for type '{enumType}': {ex.Message}");
                throw;
            }
        }

        public static List<(string ActionName, string Description, string Arguments)> GetAvailableActions()
        {
            var result = new List<(string ActionName, string Description, string Arguments)>();

            foreach (var action in ActionMetadata)
            {
                BaseAction actionInstance = (BaseAction)Activator.CreateInstance(action.Value.ActionClass);
                string arguments = actionInstance.GetArguments();
                result.Add((action.Key, action.Value.Description, arguments));
            }

            return result;
        }

        public static List<(string EnumerationName, string Description)> GetAvailableEnumerations()
        {
            var result = new List<(string EnumerationName, string Description)>();

            foreach (var enumeration in EnumerationMetadata)
            {
                result.Add((enumeration.Key, enumeration.Value.Description));
            }

            return result;
        }
    }
}
