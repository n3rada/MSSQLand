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
            { "config", (typeof(Configure), "Use sp_configure to modify settings.") },
            { "kill", (typeof(Kill), "Terminate running SQL commands by session ID or all.") },
            { "createsysadmin", (typeof(CreateSysadmin), "Create a SQL login with sysadmin privileges (default: backup_adm).") },

            // ═══════════════════════════════════════════════════════════════════════════════
            // DATABASE ACTIONS (MSSQLand.Actions.Database)
            // ═══════════════════════════════════════════════════════════════════════════════
            { "info", (typeof(Info), "Retrieve information about the DBMS server.") },
            { "whoami", (typeof(Whoami), "Retrieve information about the current user.") },
            { "databases", (typeof(Databases), "List available databases.") },
            { "tables", (typeof(Tables), "List tables in a database.") },
            { "rows", (typeof(Rows), "Retrieve rows from a table.") },
            { "procedures", (typeof(Procedures), "List stored procedures, read their definitions, or execute them with arguments.") },
            { "users", (typeof(Users), "List database users.") },
            { "rolemembers", (typeof(RoleMembers), "List members of a specific server role (e.g., sysadmin).") },
            { "permissions", (typeof(Permissions), "Enumerate permissions.") },
            { "search", (typeof(Search), "Search for specific keyword in database columns and data (supports * for all databases).") },
            { "impersonate", (typeof(Impersonation), "Check and perform user impersonation.") },
            { "monitor", (typeof(Monitor), "List running SQL commands.") },
            { "query", (typeof(Query), "Execute a custom T-SQL query.") },
            { "queryall", (typeof(QueryAll), "Execute a custom T-SQL query across all databases using sp_MSforeachdb.") },

            // ═══════════════════════════════════════════════════════════════════════════════
            // DOMAIN ACTIONS (MSSQLand.Actions.Domain)
            // ═══════════════════════════════════════════════════════════════════════════════
            { "domsid", (typeof(DomainSid), "Retrieve the domain SID using DEFAULT_DOMAIN and SUSER_SID.") },
            { "ridcycle", (typeof(RidCycle), "Enumerate domain users by cycling through RIDs using SUSER_SNAME.") },
            { "groupmembers", (typeof(GroupMembers), "Retrieve members of a specific Active Directory group (e.g., DOMAIN\\IT).") },
            
            // ═══════════════════════════════════════════════════════════════════════════════
            // EXECUTION ACTIONS (MSSQLand.Actions.Execution)
            // ═══════════════════════════════════════════════════════════════════════════════
            { "exec", (typeof(XpCmd), "Execute commands using xp_cmdshell.") },
            { "pwsh", (typeof(PowerShell), "Execute PowerShell commands.") },
            { "pwshdl", (typeof(RemotePowerShellExecutor), "Download and execute a PowerShell script.") },
            { "ole", (typeof(ObjectLinkingEmbedding), "Executes the specified command using OLE Automation Procedures.") },
            { "clr", (typeof(ClrExecution), "Deploy and execute CLR assemblies.") },
            { "agents", (typeof(Agents), "Interact with and manage SQL Server Agent jobs.") },
            { "oledb-providers", (typeof(OleDbProvidersInfo), "Retrieve detailed configuration and properties of OLE DB providers.") },
            { "xprocs", (typeof(ExtendedProcs), "Enumerate extended stored procedures available on the server.") },

            // ═══════════════════════════════════════════════════════════════════════════════
            // FILESYSTEM ACTIONS (MSSQLand.Actions.FileSystem)
            // ═══════════════════════════════════════════════════════════════════════════════
            { "read", (typeof(FileRead), "Read system file contents (e.g., C:\\Windows\\System32\\drivers\\etc\\hosts).") },

            
            // ═══════════════════════════════════════════════════════════════════════════════
            // NETWORK ACTIONS (MSSQLand.Actions.Network)
            // ═══════════════════════════════════════════════════════════════════════════════
            { "links", (typeof(Links), "Retrieve linked server information.") },
            { "linkmap", (typeof(LinkedServerExplorer), "Enumerate all possible linked server chains and access paths.") },
            { "rpc", (typeof(RemoteProcedureCall), "Enable or disable RPC on a server.") },
            { "adsi", (typeof(AdsiCredentialExtractor), "Enumerate ADSI linked servers, extract credentials, or impersonate users via ADSI exploitation.") },
            { "smbcoerce", (typeof(SmbCoerce), "Coerce SMB authentication via xp_dirtree to capture NTLM hashes or relay attacks.") }


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
    }
}
