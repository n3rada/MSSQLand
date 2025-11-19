using System;
using System.Collections.Generic;
using MSSQLand.Actions;
using MSSQLand.Actions.Network;
using MSSQLand.Actions.Database;
using MSSQLand.Actions.FileSystem;
using MSSQLand.Actions.Execution;
using MSSQLand.Actions.Domain;
using MSSQLand.Actions.Administration;

namespace MSSQLand.Utilities
{
    public static class ActionFactory
    {
        private static readonly Dictionary<string, (Type ActionClass, string Description)> ActionMetadata =
        new()
        {
            // ═══════════════════════════════════════════════════════════════════════════════
            // ADMINISTRATION ACTIONS (MSSQLand.Actions.Administration)
            // ═══════════════════════════════════════════════════════════════════════════════
            { "config", (typeof(Configure), "Enable or disable SQL Server configuration options using sp_configure.") },
            { "kill", (typeof(Kill), "Terminate SQL Server sessions by session ID or kill all running sessions.") },
            { "createuser", (typeof(CreateUser), "Create a SQL login with specified server role privileges (default: sysadmin).") },
            { "sessions", (typeof(Sessions), "Display active SQL Server sessions with login and connection information.") },
            { "adsi", (typeof(AdsiManager), "Manage ADSI linked servers: list, create, or delete ADSI providers.") },


            // ═══════════════════════════════════════════════════════════════════════════════
            // DATABASE ACTIONS (MSSQLand.Actions.Database)
            // ═══════════════════════════════════════════════════════════════════════════════
            { "info", (typeof(Info), "Retrieve detailed information about the SQL Server instance.") },
            { "whoami", (typeof(Whoami), "Display current user context, roles, and accessible databases.") },
            { "databases", (typeof(Databases), "List all available databases.") },
            { "tables", (typeof(Tables), "List all tables in a specified database.") },
            { "rows", (typeof(Rows), "Retrieve and display rows from a specified table.") },
            { "procedures", (typeof(Procedures), "List, read, or execute stored procedures.") },
            { "xprocs", (typeof(ExtendedProcs), "Enumerate available extended stored procedures on the server.") },
            { "users", (typeof(Users), "List all database users.") },
            { "roles", (typeof(Roles), "List all database roles and their members in the current database.") },
            { "rolemembers", (typeof(RoleMembers), "List members of a specific server role (e.g., sysadmin).") },
            { "permissions", (typeof(Permissions), "Enumerate user and role permissions.") },
            { "configs", (typeof(Configs), "List security-sensitive configuration options with their activation status.") },
            { "search", (typeof(Search), "Search for keywords in column names and data across databases.") },
            { "impersonate", (typeof(Impersonation), "Check impersonation permissions for SQL logins and Windows principals.") },
            { "monitor", (typeof(Monitor), "Display currently running SQL commands and active sessions.") },
            { "query", (typeof(Query), "Execute a custom T-SQL query.") },
            { "oledb-providers", (typeof(OleDbProvidersInfo), "Retrieve information about installed OLE DB providers and their configurations.") },
            { "queryall", (typeof(QueryAll), "Execute a custom T-SQL query across all databases using sp_MSforeachdb.") },

            // ═══════════════════════════════════════════════════════════════════════════════
            // DOMAIN ACTIONS (MSSQLand.Actions.Domain)
            // ═══════════════════════════════════════════════════════════════════════════════
            { "ad-domain", (typeof(AdDomain), "Retrieve the domain SID using DEFAULT_DOMAIN() and SUSER_SID().") },
            { "ad-sid", (typeof(AdSid), "Retrieve the current user's SID using SUSER_SID().") },
            { "ad-groups", (typeof(AdGroups), "Retrieve Active Directory group memberships for the current user using xp_logininfo.") },
            { "ridcycle", (typeof(RidCycle), "Enumerate domain users by RID cycling using SUSER_SNAME().") },
            { "ad-members", (typeof(AdMembers), "Retrieve members of an Active Directory group (e.g., DOMAIN\\Domain Admins).") },
            
            // ═══════════════════════════════════════════════════════════════════════════════
            // EXECUTION ACTIONS (MSSQLand.Actions.Execution)
            // ═══════════════════════════════════════════════════════════════════════════════
            { "exec", (typeof(XpCmd), "Execute operating system commands using xp_cmdshell.") },
            { "pwsh", (typeof(PowerShell), "Execute PowerShell scripts via xp_cmdshell.") },
            { "pwshdl", (typeof(RemotePowerShellExecutor), "Download and execute a remote PowerShell script from a URL.") },
            { "ole", (typeof(ObjectLinkingEmbedding), "Execute operating system commands using OLE Automation Procedures.") },
            { "clr", (typeof(ClrExecution), "Deploy and execute custom CLR assemblies.") },
            { "agents", (typeof(Agents), "Manage and interact with SQL Server Agent jobs.") },

            // ═══════════════════════════════════════════════════════════════════════════════
            // FILESYSTEM ACTIONS (MSSQLand.Actions.FileSystem)
            // ═══════════════════════════════════════════════════════════════════════════════
            { "read", (typeof(FileRead), "Read file contents from the server's file system.") },
            { "tree", (typeof(Tree), "Display directory tree structure in Linux tree-style format.") },

            
            // ═══════════════════════════════════════════════════════════════════════════════
            // NETWORK ACTIONS (MSSQLand.Actions.Network)
            // ═══════════════════════════════════════════════════════════════════════════════
            { "links", (typeof(Links), "Enumerate linked servers and their configuration.") },
            { "linkmap", (typeof(LinkedServerExplorer), "Map all possible linked server chains and execution paths.") },
            { "rpc", (typeof(RemoteProcedureCall), "Enable or disable RPC (Remote Procedure Calls) on linked servers.") },
            { "adsiquery", (typeof(AdsiQuery), "Query Active Directory via ADSI using fully qualified domain name (auto-creates temp server if needed).") },
            { "adsicreds", (typeof(AdsiCredentialExtractor), "Extract credentials from ADSI linked servers by intercepting LDAP authentication.") },
            { "smbcoerce", (typeof(SmbCoerce), "Force SMB authentication to a specified UNC path to capture time-limited Net-NTLMv2 challenge/response.") }
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

        public static List<(string ActionName, string Description, List<string> Arguments, string Category)> GetAvailableActions()
        {
            var result = new List<(string ActionName, string Description, List<string> Arguments, string Category)>();

            foreach (var action in ActionMetadata)
            {
                BaseAction actionInstance = (BaseAction)Activator.CreateInstance(action.Value.ActionClass);
                List<string> arguments = actionInstance.GetArguments();
                
                // Extract category from namespace (last part after the last dot)
                string fullNamespace = action.Value.ActionClass.Namespace;
                string category = fullNamespace.Substring(fullNamespace.LastIndexOf('.') + 1);
                
                result.Add((action.Key, action.Value.Description, arguments, category));
            }

            return result;
        }

        /// <summary>
        /// Gets the Type of an action by its name.
        /// </summary>
        /// <param name="actionName">The name of the action.</param>
        /// <returns>The Type of the action class, or null if not found.</returns>
        public static Type GetActionType(string actionName)
        {
            if (ActionMetadata.TryGetValue(actionName.ToLower(), out var metadata))
            {
                return metadata.ActionClass;
            }
            return null;
        }
    }
}
